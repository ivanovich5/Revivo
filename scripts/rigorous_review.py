"""Offline content validation and non-destructive organization of a recovery case.
Never executes recovered files, macros or links. No network APIs. New output only.
Validation is structural, not proof of equivalence to a lost original.
"""
import collections
from concurrent.futures import ThreadPoolExecutor, as_completed
import csv
import hashlib
import json
import os
import posixpath
from urllib.parse import unquote
from pathlib import Path
from local_paths import local_path, display_path
import re
import shutil
import struct
import subprocess
import sys
import time
import unicodedata
import warnings
import zipfile
import xml.etree.ElementTree as ET

TOPICS={
 'Educacion': ['alumno','alumnos','docente','docentes','aprendizaje','evaluacion','escolar','escuela','educacion','pedagogia','didactica','estudiantes','ensenanza','curricular'],
 'Finanzas_y_contabilidad':['factura','facturas','importe','iva','contabilidad','contable','presupuesto','financiero','financieros','impuesto','impuestos','balance'],
 'Leyes_y_tramites':['juridico','juridica','reglamento','ley','decreto','contrato','tribunal','articulo','constitucion','demandante'],
 'Salud':['paciente','pacientes','clinico','clinica','medico','medica','diagnostico','enfermedad','tratamiento','hospital'],
 'Tecnologia':['software','hardware','servidor','programacion','algoritmo','codigo','database','javascript','python','computadora'],
 'Administracion_y_trabajo':['empleado','empleados','laboral','nomina','recursos humanos','organigrama','administrativo','administracion','empresa','empresas'],
 'Literatura':['novela','poema','poesia','poemas','cuento','cuentos','narrador','literatura','literario'],
}

def topic(text):
    s=''.join(c for c in unicodedata.normalize('NFKD',text.lower()) if not unicodedata.combining(c))
    counts=collections.Counter(re.findall(r'[a-z]{3,}',s)); scores=[]
    for label,terms in TOPICS.items():
        present={w:counts[w] for w in terms if counts[w]}
        scores.append((sum(min(c,8) for c in present.values()),len(present),label))
    scores.sort(reverse=True)
    best=scores[0]
    if best[0]>=8 and best[1]>=3 and best[0]>=scores[1][0]*1.4:
        return {'topic':best[2],'topicBasis':'Reglas por palabras del texto; clasificación orientativa','topicScore':best[0]}
    return {'topic':'Sin_tema_concluyente','topicBasis':'Texto insuficiente o temas ambiguos'}

def sha(path):
    h=hashlib.sha256()
    with local_path(path).open('rb') as f:
        while b:=f.read(1024*1024): h.update(b)
    return h.hexdigest()

def atomic_json(path,obj):
    p=local_path(path); temp=p.with_suffix(p.suffix+'.tmp')
    temp.write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf-8'); temp.replace(p)

def xml(data):
    if b'<!DOCTYPE' in data.upper() or b'<!ENTITY' in data.upper(): raise ValueError('DTD/entidades no admitidas')
    return ET.fromstring(data)

def inspect_file(path,checkpoint=None):
    p=local_path(path); size=p.stat().st_size
    with p.open('rb') as f: head=f.read(65536)
    out={'status':'04_Sin_validar','type':'Otros','identifiedExtension':p.suffix,'reason':'Sin validador estructural completo para este contenido'}
    text=''
    if p.suffix.lower() in {'.lnk','.exe','.dll','.bat','.cmd','.ps1','.vbs','.js','.scr'} or head.startswith(b'L\x00\x00\x00\x01\x14\x02\x00') or head.startswith(b'MZ'):
        return {**out,'status':'06_No_copiar_contenido_activo','type':'Accesos_o_programas','reason':'No ejecutar ni copiar a la colección organizada; conservar sólo referencia al original'}
    if head.startswith(b'%PDF-'):
        out.update(type='PDF',identifiedExtension='.pdf')
        if p.suffix.lower()=='.ai': out.update(type='Diseno_Illustrator_PDF',identifiedExtension='.ai')
        if size>512*1024*1024: return {**out,'reason':'Supera 512 MiB: revisión manual pendiente'}
        import fitz
        fitz.TOOLS.mupdf_display_errors(False);fitz.TOOLS.mupdf_display_warnings(False);fitz.TOOLS.reset_mupdf_warnings()
        with fitz.open(p) as doc:
            out.update(pages=doc.page_count,repairedOnOpen=doc.is_repaired,pagesChecked=0)
            if doc.needs_pass: return {**out,'reason':'PDF protegido: contenido sin validar'}
            if doc.page_count<1: return {**out,'status':'02_Con_avisos_o_danos','reason':'El parser no encuentra páginas en el PDF'}
            if doc.page_count>1500: return {**out,'reason':'Supera el límite de 1500 páginas; comprobación pendiente'}
            out['activeContentDetected']=False
            # Detect common active PDF actions without executing any action.
            if doc.xref_length()<=100000:
                for x in range(1,doc.xref_length()):
                    obj=doc.xref_object(x,compressed=True)
                    if re.search(r'/(?:JavaScript|JS|Launch|RichMedia)\b',obj): out['activeContentDetected']=True;break
            else: out['activeContentScanLimited']=True
            errors=[]
            for i in range(doc.page_count):
                try:
                    page=doc.load_page(i)
                    if not 0<page.rect.width<=20000 or not 0<page.rect.height<=20000: raise ValueError('Dimensión de página fuera del límite')
                    scale=min(1/3,1200/max(page.rect.width,page.rect.height))
                    pix=page.get_pixmap(matrix=fitz.Matrix(scale,scale),colorspace=fitz.csRGB,alpha=False)
                    if pix.width<1 or pix.height<1: raise ValueError('Página sin imagen renderizable')
                    del pix
                    if len(text)<150000: text+=page.get_text()[:150000-len(text)]+'\n'
                    out['pagesChecked']+=1
                except Exception as e:
                    errors.append({'page':i+1,'error':str(e)[:200]})
                    if len(errors)>=10: break
                if checkpoint and i%10==0: atomic_json(checkpoint,{**out,**topic(text),'reason':'Comprobación en curso'})
            messages=fitz.TOOLS.mupdf_warnings()
            with p.open('rb') as f:
                f.seek(max(0,size-2048)); ended=b'%%EOF' in f.read()
            out.update(renderErrors=errors,parserWarnings=messages[:2000],endMarkerPresent=ended,**topic(text))
            if errors or out['pagesChecked']!=out['pages']:
                out.update(status='02_Con_avisos_o_danos',reason='Una o más páginas no superaron el renderizado')
            elif out['repairedOnOpen'] or messages or not ended or out.get('activeContentDetected') or out.get('activeContentScanLimited'):
                out.update(status='02_Con_avisos_o_danos',reason='Páginas renderizadas, pero hay reparaciones, avisos o límites/alertas de estructura')
            else: out.update(status='01_Comprobacion_superada',reason='Todas las páginas renderizadas a baja resolución; sin reparación ni avisos del parser. No acredita equivalencia al original')
        return out
    if head.startswith(b'PK\x03\x04'):
        out.update(type='ZIP',identifiedExtension='.zip')
        with zipfile.ZipFile(p) as z:
            infos=z.infolist(); names=[i.filename for i in infos]; name_set=set(names)
            if len(infos)>10000 or sum(i.file_size for i in infos)>256*1024*1024 or any(i.file_size>64*1024*1024 for i in infos): return {**out,'reason':'Límite de expansión ZIP; no se extrajo el contenido'}
            if any(i.flag_bits&1 for i in infos): return {**out,'reason':'ZIP cifrado: no validado'}
            if len(names)!=len(name_set): return {**out,'status':'02_Con_avisos_o_danos','reason':'Entradas ZIP con nombres duplicados'}
            if any(n.startswith(('/','\\')) or '..' in n.replace('\\','/').split('/') or ':' in n for n in names): return {**out,'status':'02_Con_avisos_o_danos','reason':'Rutas internas inseguras; no se extrajo el contenido'}
            bad=z.testzip()
            if bad: return {**out,'status':'02_Con_avisos_o_danos','reason':'CRC incorrecto en miembro ZIP'}
            family=None
            for member,kind,ext,rootname in [('word/document.xml','Word','.docx','document'),('xl/workbook.xml','Excel','.xlsx','workbook'),('ppt/presentation.xml','PowerPoint','.pptx','presentation')]:
                if member in name_set:
                    family=member; out.update(type=kind,identifiedExtension=ext)
                    if xml(z.read(member)).tag.split('}')[-1]!=rootname: raise ValueError('Raíz XML incoherente con el formato')
                    break
            if family:
                if '[Content_Types].xml' not in name_set or '_rels/.rels' not in name_set: raise ValueError('Faltan registros obligatorios del paquete Office')
                package_rels=xml(z.read('_rels/.rels'))
                main_targets=[unquote(e.attrib.get('Target','')).lstrip('/') for e in package_rels
                              if e.attrib.get('Type','').endswith('/officeDocument') and e.attrib.get('TargetMode')!='External']
                if main_targets != [family]: raise ValueError('La relación principal Office falta o no coincide con su contenido')
                content_types=xml(z.read('[Content_Types].xml'))
                if not any(e.attrib.get('PartName')=='/'+family and e.attrib.get('ContentType','').endswith('main+xml') for e in content_types):
                    raise ValueError('Falta el tipo de contenido de la parte principal Office')
                checked=0; external=0
                for i in infos:
                    if i.filename.endswith(('.xml','.rels')):
                        tree=xml(z.read(i)); checked+=1
                        if len(text)<150000: text+=' '.join(tree.itertext())[:150000-len(text)]+'\n'
                        if i.filename.endswith('.rels'):
                            external+=sum(e.attrib.get('TargetMode')=='External' for e in tree.iter())
                            base=posixpath.dirname(posixpath.dirname(i.filename)) if i.filename!='_rels/.rels' else ''
                            for rel in tree:
                                if rel.attrib.get('TargetMode')=='External':continue
                                target=unquote(rel.attrib.get('Target','')).split('#',1)[0]
                                resolved=posixpath.normpath(target.lstrip('/') if target.startswith('/') else posixpath.join(base,target))
                                if not target or resolved not in name_set:raise ValueError('Relación Office apunta a una parte ausente: '+resolved[:150])
                macro=any('vbaproject' in n.lower() for n in names)
                out.update(xmlPartsChecked=checked,externalRelationships=external,macrosDetected=macro,**topic(text))
                if macro or external:
                    out.update(status='02_Con_avisos_o_danos',reason='CRC y XML comprobados; contiene macros o vínculos externos (no ejecutados)')
                else: out.update(status='01_Comprobacion_superada',reason='CRC de todos los miembros y sintaxis de todos los XML correctos; no se validó visualmente en Office')
            elif 'mimetype' in name_set and z.read('mimetype')==b'application/epub+zip':
                out.update(type='Libros_EPUB',identifiedExtension='.epub')
                if 'META-INF/container.xml' not in name_set: raise ValueError('EPUB sin container.xml')
                for n in names:
                    if n.endswith(('.xml','.opf','.xhtml','.ncx')):
                        tree=xml(z.read(n))
                        if len(text)<150000: text+=' '.join(tree.itertext())[:150000-len(text)]+'\n'
                out.update(status='01_Comprobacion_superada',reason='Contenedor EPUB con CRC y XML comprobados; no se validó maquetación',**topic(text))
            else: out.update(status='01_Comprobacion_superada',reason='ZIP completo con CRC correcto; contenido interno no ejecutado ni validado semánticamente')
        return out
    if head.startswith((b'\xff\xd8\xff',b'\x89PNG\r\n\x1a\n',b'GIF87a',b'GIF89a')):
        from PIL import Image
        Image.MAX_IMAGE_PIXELS=40000000;warnings.simplefilter('error')
        with Image.open(p) as img:
            fmt=img.format; dimensions=img.size; img.verify()
        with Image.open(p) as img:
            frames=getattr(img,'n_frames',1)
            if frames>100: return {**out,'type':'Imagenes','reason':'Más de 100 fotogramas: sin validación completa'}
            for i in range(frames):img.seek(i);img.load()
        return {**out,'status':'01_Comprobacion_superada','type':'Imagenes','identifiedExtension':{'JPEG':'.jpg','PNG':'.png','GIF':'.gif'}.get(fmt,p.suffix),'reason':'Estructura verificada y todos los fotogramas decodificados','dimensions':dimensions,'frames':frames}
    if head[:4]==b'RIFF' and head[8:12]==b'WAVE':
        import wave
        with wave.open(str(p),'rb') as w:
            expected=w.getnframes()*w.getnchannels()*w.getsampwidth(); actual=0
            while data:=w.readframes(65536):actual+=len(data)
        ok=actual==expected and struct.unpack_from('<I',head,4)[0]+8<=size
        return {**out,'status':'01_Comprobacion_superada' if ok else '02_Con_avisos_o_danos','type':'Audio_WAV','identifiedExtension':'.wav','reason':'Datos PCM completos según cabecera; no se comprobó contenido audible' if ok else 'Longitud WAV incoherente'}
    if head.startswith(b'\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1'):
        return {**out,'type':'Office_antiguo_u_OLE','reason':'Firma OLE reconocida; sin validación interna suficiente. No se considera documento íntegro'}
    if head.startswith(b'Rar!\x1a\x07'):
        return {**out,'type':'Comprimidos_RAR','reason':'Firma RAR reconocida; falta comprobar todos los miembros y sus CRC'}
    if head.startswith((b'\x00\x05\x16\x07',b'\x00\x05\x16\x00')):
        return {**out,'type':'Metadatos_Apple','reason':'Contenedor AppleDouble/AppleSingle reconocido; no es necesariamente un documento independiente'}
    if p.suffix.lower() in {'.txt','.ini'}:
        if size>16*1024*1024: return {**out,'type':'Texto','reason':'Texto supera el límite de análisis'}
        b=p.read_bytes()
        markers=re.findall(rb'\b\d+\s+\d+\s+obj\b|\bendobj\b|\bendstream\b|/Type\s*/(?:Page|Font)\b|\bstartxref\b|%%EOF',b)
        if len(markers)>=2 and (b'endobj' in b or b'endstream' in b or b'startxref' in b):
            return {**out,'status':'03_Fragmentos','type':'Restos_PDF','identifiedExtension':'.txt','reason':'Texto con sintaxis interna PDF sin cabecera de documento completo','pdfMarkers':len(markers)}
        encoding=None
        for enc in (['utf-16'] if b.startswith((b'\xff\xfe',b'\xfe\xff')) else ['utf-8-sig','cp1252']):
            try:text=b.decode(enc);encoding=enc;break
            except UnicodeError:pass
        if encoding is None: return {**out,'status':'03_Fragmentos','type':'Texto_no_interpretable','reason':'No decodifica en las codificaciones comprobadas'}
        fraction=sum(c.isprintable() or c in '\r\n\t' for c in text)/max(1,len(text))
        if fraction<0.98: return {**out,'status':'03_Fragmentos','type':'Texto_con_datos_binarios','reason':'Contiene una proporción alta de controles o datos no textuales','printableFraction':fraction}
        if re.search(r'<\?xml|<(?:html|w:|x:|office:|worksheet|svg)\b',text,re.I):
            kind='Texto_XML_HTML'
        elif re.search(r'(?i)\b(?:powershell|wscript\.shell|createobject|cmd\.exe|eval\s*\()',text): kind='Texto_con_instrucciones'
        else:kind='Texto_legible'
        return {**out,'status':'04_Sin_validar','type':kind,'identifiedExtension':'.txt','reason':'Texto decodificable; no se puede acreditar inicio, fin ni que fuera un archivo independiente','encoding':encoding,'characters':len(text),**topic(text[:150000])}
    if p.suffix.lower() in {'.pdf','.docx','.xlsx','.pptx','.jpg','.jpeg','.png','.wav','.epub'}:
        return {**out,'status':'02_Con_avisos_o_danos','type':'Tipo_declarado_no_coincide',
                'reason':'El nombre declara '+p.suffix.lower()+' pero no se reconoce su cabecera; posible fragmento, contenido sobrescrito o asociación obsoleta. No se considera un documento de ese tipo'}
    return out

def worker(path,check):
    try: result=inspect_file(path,check)
    except (ImportError, MemoryError) as e: result={'status':'04_Sin_validar','type':'Validacion_pendiente','reason':'Validador no disponible o recursos insuficientes: '+str(e)[:300]}
    except Exception as e: result={'status':'02_Con_avisos_o_danos','type':'Formato_no_validado','reason':type(e).__name__+': '+str(e)[:500]}
    atomic_json(check,result)

def checked(item,checks):
    p=local_path(item['path']); digest=item['sha256']; checkpoint=checks/(digest+'.json')
    if sha(p).lower()!=digest.lower(): return {**item,'status':'04_Sin_validar','type':'Cambio_detectado','reason':'El archivo cambió desde el inventario; no se copia'}
    if not checkpoint.exists():
        try:
            subprocess.run([sys.executable,__file__,'--worker',str(p),str(checkpoint)],timeout=30,
                           stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0),check=True)
        except (subprocess.TimeoutExpired,subprocess.CalledProcessError) as e:
            prior=json.loads(checkpoint.read_text(encoding='utf-8')) if checkpoint.exists() else {}
            atomic_json(checkpoint,{**prior,'status':'04_Sin_validar','type':prior.get('type','Validacion_pendiente'),'reason':'Proceso limitado a 30 segundos o interrumpido; no se considera comprobado','processError':type(e).__name__})
    return {**item,**json.loads(checkpoint.read_text(encoding='utf-8'))}

def organize(job):
    job=local_path(job).resolve(); inventory=json.loads((job/'inventario-recuperado.json').read_text(encoding='utf-8'))
    root=job/('organizados-'+time.strftime('%Y%m%d-%H%M%S'));root.mkdir(exist_ok=False)
    checks=root/'informes'/'validaciones';checks.mkdir(parents=True)
    groups=collections.defaultdict(list)
    for item in inventory['files']:
        p=local_path(item['path']).resolve()
        if not p.is_relative_to(job) or p.is_symlink():raise ValueError('Origen fuera del caso')
        groups[item['sha256']].append(item)
    results=[]
    with ThreadPoolExecutor(max_workers=4) as pool:
        pending=[pool.submit(checked,items[0],checks) for items in groups.values()]
        for future in as_completed(pending):
            results.append(future.result())
            if len(results)%25==0:
                atomic_json(root/'progreso.json',{'phase':'Validando','done':len(results),'total':len(groups)})
                print(f'Validados {len(results)}/{len(groups)}',flush=True)
    copied=0
    for result in sorted(results,key=lambda r:r['path']):
        p=local_path(result['path']); result['duplicateSources']=[i['path'] for i in groups[result['sha256']][1:]]
        if result['status']=='06_No_copiar_contenido_activo' or result['type']=='Cambio_detectado':continue
        parts=[result['status'],result['type']]
        if result.get('topic') and result['status'] in {'01_Comprobacion_superada','02_Con_avisos_o_danos','04_Sin_validar'}:parts.append(result['topic'])
        directory=root.joinpath(*parts);directory.mkdir(parents=True,exist_ok=True)
        # Generated name; no text from inside a recovered document used as a path.
        name=p.stem[:90]+'__'+result['sha256'][:12]+result.get('identifiedExtension',p.suffix)
        destination=directory/name
        if not destination.resolve().is_relative_to(root):raise ValueError('Destino fuera del caso')
        with p.open('rb') as source,destination.open('xb') as target:shutil.copyfileobj(source,target,1024*1024)
        digest=sha(destination)
        if digest!=result['sha256']:raise ValueError('La copia no coincide con el inventario')
        result['organizedPath']=str(destination); result['copySha256Verified']=True;copied+=1
        if copied%100==0:print(f'Organizados y verificados {copied}',flush=True)
    summary={'output':str(root),'inputFiles':len(inventory['files']),'uniqueContents':len(groups),
             'duplicateCopiesNotRepeated':len(inventory['files'])-len(groups),'organizedFiles':copied,
             'byStatus':dict(collections.Counter(r['status'] for r in results)),
             'byType':dict(collections.Counter(r['type'] for r in results)),
             'byTopic':dict(collections.Counter(r.get('topic','No_aplica') for r in results)),
             'notAvailableInEarlierExport':len(inventory['unavailableFiles']),
             'limitations':['Clasificación temática orientativa, no carpetas originales.',
               'Comprobación superada no prueba integridad histórica ni equivalencia al original.',
               'No se ejecutan macros, vínculos ni contenido activo; tampoco es un análisis antivirus.',
               'Archivos con límites o errores quedan separados; originales sin modificar.',
               'Los duplicados exactos se referencian en el manifiesto, no se vuelven a copiar.']}
    atomic_json(root/'informes'/'manifest.json',{'summary':summary,'files':results})
    with (root/'informes'/'catalogo.csv').open('w',encoding='utf-8-sig',newline='') as f:
        fields=['path','organizedPath','size','sha256','status','type','topic','reason','pages','pagesChecked','copySha256Verified']
        writer=csv.DictWriter(f,fieldnames=fields,extrasaction='ignore');writer.writeheader();writer.writerows(results)
    atomic_json(root/'resumen.json',summary);atomic_json(job/'ultima-organizacion.json',summary)
    atomic_json(root/'progreso.json',{'phase':'Finalizado','done':len(groups),'total':len(groups)})
    print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)

if __name__=='__main__':
    if sys.argv[1]=='--worker':worker(sys.argv[2],sys.argv[3])
    else:organize(sys.argv[1])
