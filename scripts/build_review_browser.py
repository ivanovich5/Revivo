"""Build offline previews and explicitly partial derivatives. Sources are read only.
Each parser runs in a time-limited child process; no documents/macros are executed.
"""
import concurrent.futures, hashlib, json, subprocess, sys, time, zipfile
from pathlib import Path
from local_paths import local_path, display_path
import xml.etree.ElementTree as ET

def digest(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        while b:=f.read(1048576): h.update(b)
    return h.hexdigest()

def safe_xml(b):
    if b'<!DOCTYPE' in b.upper() or b'<!ENTITY' in b.upper(): raise ValueError('XML con entidades no admitido')
    return ET.fromstring(b)

def spreadsheet(z):
    import posixpath,re
    ns={'m':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    strings=[]
    if 'xl/sharedStrings.xml' in z.namelist():
        strings=[''.join(e.itertext()) for e in safe_xml(z.read('xl/sharedStrings.xml')).findall('m:si',ns)]
    rels={e.attrib.get('Id'):e.attrib.get('Target','') for e in safe_xml(z.read('xl/_rels/workbook.xml.rels')) if e.attrib.get('TargetMode')!='External'}
    sheets=[]
    for sheet in safe_xml(z.read('xl/workbook.xml')).findall('m:sheets/m:sheet',ns)[:20]:
        rid=sheet.attrib.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id')
        target=rels.get(rid,''); target=target.lstrip('/') if target.startswith('/') else posixpath.normpath('xl/'+target)
        if not target.startswith('xl/') or target not in z.namelist(): continue
        if z.getinfo(target).file_size>16*1024*1024: continue
        rows=[]
        for row in safe_xml(z.read(target)).findall('m:sheetData/m:row',ns)[:200]:
            cells=['']*30
            for cell in row:
                match=re.match(r'^([A-Z]+)',cell.attrib.get('r',''))
                if not match: continue
                col=0
                for c in match[1]:col=col*26+ord(c)-64
                if col>30: continue
                v=cell.find('m:v',ns);value=v.text or '' if v is not None else ''
                kind=cell.attrib.get('t')
                if kind=='s': value=strings[int(value)] if value.isdigit() and int(value)<len(strings) else '[Referencia de texto inválida]'
                elif kind=='inlineStr':value=''.join(e.text or '' for e in cell.findall('.//m:t',ns))
                elif kind=='b':value='VERDADERO' if value=='1' else 'FALSO'
                if cell.find('m:f',ns) is not None:value+=' [valor guardado de fórmula]' if value else '[Fórmula sin resultado guardado]'
                cells[col-1]=value[:2000]
            rows.append([row.attrib.get('r','')]+cells)
        sheets.append({'name':sheet.attrib.get('name','Hoja'),'rows':rows})
    return sheets

def process(item, dest):
    p=local_path(item['path']); dest=local_path(dest); dest.mkdir(parents=True,exist_ok=True)
    if digest(p).lower()!=item['sha256'].lower(): raise ValueError('La huella del archivo cambió')
    result={'text':'','images':[], 'partial':False, 'note':'', 'pagesTotal':0,'pagesSaved':0}
    group=item['group']; partial=item['status']!='01_Comprobacion_superada'
    if group in ('PDF','Diseno_Illustrator'):
        import fitz
        fitz.TOOLS.mupdf_display_errors(False); fitz.TOOLS.mupdf_display_warnings(False)
        with fitz.open(p) as doc:
            if doc.needs_pass: raise ValueError('Documento protegido con contraseña')
            result['pagesTotal']=doc.page_count
            limit=min(doc.page_count,20)
            errors=[]; texts=[]
            for n in range(limit):
                try:
                    page=doc.load_page(n); w,h=page.rect.width,page.rect.height
                    if not 0<w<=20000 or not 0<h<=20000: raise ValueError('Dimensiones fuera de límite')
                    pix=page.get_pixmap(matrix=fitz.Matrix(min(1.5,1100/max(w,h)),min(1.5,1100/max(w,h))),alpha=False)
                    name=f'pagina-{n+1:04}.png'; pix.save(dest/name)
                    result['images'].append(name); result['pagesSaved']+=1
                    texts.append(f'\n[Página {n+1}]\n'+page.get_text()[:20000])
                except Exception as e: errors.append(f'Página {n+1}: {str(e)[:150]}')
            result['text']=''.join(texts)[:200000]
            result['partial']=partial and bool(result['images'] or result['text'].strip())
            result['note']=(f"{result['pagesSaved']} de {doc.page_count} páginas representadas. "
                + ('Extracción parcial: máximo 20 páginas; puede faltar contenido incluso en páginas visibles. ' if partial else 'Vista previa de hasta 20 páginas. ')
                + 'No conserva edición, adjuntos ni formularios. '+'; '.join(errors))
    elif group=='Imagenes':
        from PIL import Image
        Image.MAX_IMAGE_PIXELS=40000000
        with Image.open(p) as im:
            im.thumbnail((1100,1100)); im.convert('RGB').save(dest/'imagen.png')
        result['images']=['imagen.png'];result['note']='Vista reducida del primer fotograma.'
    elif group in ('DOCX','XLSX','PPTX'):
        with zipfile.ZipFile(p) as z:
            infos=z.infolist()
            if len(infos)>10000 or sum(i.file_size for i in infos)>256*1024*1024: raise ValueError('Paquete demasiado grande')
            if group=='DOCX': names=['word/document.xml']
            elif group=='PPTX': names=sorted(n.filename for n in infos if n.filename.startswith('ppt/slides/slide') and n.filename.endswith('.xml'))[:20]
            else: names=['xl/workbook.xml','xl/sharedStrings.xml']+[n.filename for n in infos if n.filename.startswith('xl/worksheets/sheet') and n.filename.endswith('.xml')][:3]
            parts=[]; errors=[]
            for name in names:
                if name not in z.namelist(): continue
                try:
                    if z.getinfo(name).file_size>16*1024*1024: raise ValueError('Parte demasiado grande')
                    tree=safe_xml(z.read(name))
                    if group=='XLSX':
                        # Strings and sheet names only; raw cell indices are not displayed as values.
                        values=[e.text for e in tree.iter() if e.tag.split('}')[-1]=='t' and e.text]
                        values += [e.attrib['name'] for e in tree.iter() if e.tag.split('}')[-1]=='sheet' and 'name' in e.attrib]
                    else: values=[e.text for e in tree.iter() if e.tag.split('}')[-1]=='t' and e.text]
                    parts.append(name+'\n'+'\n'.join(values)[:60000])
                except Exception as e: errors.append(name+': '+str(e)[:100])
            result['text']='\n\n'.join(parts)[:200000]
            result['partial']=partial and bool(result['text'])
            result['note']='Extracto de texto, sin maquetación. Excel: nombres de hojas y textos; no representa celdas ni calcula fórmulas. '+ '; '.join(errors)
            if group=='XLSX':
                result['sheets']=spreadsheet(z)
                result['note']='Vista de hasta 20 hojas, 200 filas y 30 columnas por hoja. Valores guardados, sin calcular fórmulas ni aplicar estilos; fechas pueden aparecer como números. '+ '; '.join(errors)
    elif group in ('Texto_sin_confirmar','Fragmentos_PDF'):
        with p.open('rb') as stream:data=stream.read(200000)
        try: result['text']=data.decode('utf-16' if data.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
        except UnicodeDecodeError: result['text']=data.decode('cp1252',errors='replace')
        result['note']='Texto sin confirmar: puede ser un fragmento. Vista limitada a 200,000 bytes; no demuestra integridad.'
    else:
        result['note']='Sin vista previa compatible. No se abre ni ejecuta el archivo recuperado.'
    if result['partial']:
        (dest/'LEEME.txt').write_text('RECUPERACIÓN PARCIAL, NO DOCUMENTO ORIGINAL COMPLETO\n'+result['note']+'\nOrigen: '+str(p)+'\nSHA256: '+item['sha256'],encoding='utf-8')
        if result['text']: (dest/'texto-parcial.txt').write_text('EXTRACTO PARCIAL\n'+result['text'],encoding='utf-8')
    (dest/'result.json').write_text(json.dumps(result,ensure_ascii=False),encoding='utf-8')

def build(review, output):
    review=local_path(review).resolve(); output=local_path(output).resolve()
    if output.exists(): raise ValueError('Usa una carpeta nueva para conservar resultados anteriores')
    output.mkdir(parents=True)
    items=json.loads((review/'informes/por-tipo.json').read_text(encoding='utf-8'))['files']
    def one(item):
        p=local_path(item['path']).resolve()
        if not p.is_relative_to(review): raise ValueError('Origen fuera de la colección')
        key=item['sha256'].lower()
        if len(key)!=64 or any(c not in '0123456789abcdef' for c in key): raise ValueError('Huella no válida')
        supported=item['group'] in ('PDF','Diseno_Illustrator','Imagenes','DOCX','XLSX','PPTX','Texto_sin_confirmar','Fragmentos_PDF')
        dest=output/('parciales' if item['status']!='01_Comprobacion_superada' else 'vistas')/key
        data={'text':'','images':[],'partial':False,'note':'Sin vista previa compatible.'}
        if supported:
            dest.mkdir(parents=True)
            req=dest/'request.json';req.write_text(json.dumps(item),encoding='utf-8')
            try:
                done=subprocess.run([sys.executable,__file__,'--worker',str(req),str(dest)],capture_output=True,timeout=45,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
                if done.returncode: raise ValueError(done.stderr.decode('utf-8',errors='replace')[-400:])
                data=json.loads((dest/'result.json').read_text(encoding='utf-8'))
            except Exception as e: data['note']='Vista no disponible: '+str(e)[:450]
        return {**item,'name':p.name,'size':p.stat().st_size,**data,'path':display_path(p),'source':display_path(p),'images':[display_path(dest/n) for n in data['images']], 'derivativesFolder':display_path(dest) if data.get('partial') else ''}
    rows=[]; started=time.time()
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        for row in pool.map(one,items):
            rows.append(row)
            if len(rows)%50==0: print(f'{len(rows)}/{len(items)}',flush=True)
    rows.sort(key=lambda r:(r['status'],r['group'],r['name']))
    summary={'files':len(rows),'previews':sum(bool(r['images'] or r['text']) for r in rows),'partialDocuments':sum(r['partial'] for r in rows),'partialPages':sum(r.get('pagesSaved',0) for r in rows if r['partial']),'seconds':round(time.time()-started)}
    (output/'catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
    (output/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    print(json.dumps(summary),flush=True)

if __name__=='__main__':
    if sys.argv[1]=='--worker': process(json.loads(local_path(sys.argv[2]).read_text(encoding='utf-8')),sys.argv[3])
    else: build(sys.argv[1],sys.argv[2])
