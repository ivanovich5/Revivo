"""Validate exported content before building previews; never modify recovered originals."""
import collections,json,sys
from concurrent.futures import ThreadPoolExecutor,as_completed
from pathlib import Path
from local_paths import local_path, display_path
from build_review_browser import build
from rigorous_review import checked,atomic_json

GROUPS={'PDF':'PDF','Diseno_Illustrator_PDF':'Diseno_Illustrator','Word':'DOCX','Excel':'XLSX','PowerPoint':'PPTX',
        'Imagenes':'Imagenes','Audio_WAV':'Audio_WAV','Texto_legible':'Texto_sin_confirmar',
        'Texto_XML_HTML':'Texto_sin_confirmar','Texto_con_instrucciones':'Texto_sin_confirmar',
        'Restos_PDF':'Fragmentos_PDF','Texto_no_interpretable':'Fragmentos_no_textuales',
        'Texto_con_datos_binarios':'Fragmentos_no_textuales','Accesos_o_programas':'Contenido_activo_sin_vista'}
EXTENSIONS={'.pdf':'PDF','.docx':'DOCX','.xlsx':'XLSX','.pptx':'PPTX','.jpg':'Imagenes','.png':'Imagenes','.gif':'Imagenes','.wav':'Audio_WAV'}

def prepare(export,output=None):
    root=local_path(export).resolve(); output=local_path(output).resolve() if output else root/'vistas-integradas'
    if not output.is_relative_to(root) or output==root:raise ValueError('Destino fuera de la exportación')
    if output.exists():raise ValueError('Usa una carpeta nueva para conservar la revisión anterior')
    report=json.loads((root/'informe.json').read_text(encoding='utf-8-sig'))
    checks=root/'informes'/('validaciones-'+output.name);checks.mkdir(parents=True,exist_ok=False)
    unique={}; unavailable=[]
    for f in report['Files']:
        p=(root/f['OutputName']).resolve();digest=(f.get('Sha256') or '').lower()
        if not p.is_relative_to(root):raise ValueError('Ruta fuera de la exportación')
        if not p.is_file() or len(digest)!=64 or any(c not in '0123456789abcdef' for c in digest):
            unavailable.append(f['OutputName']);continue
        unique.setdefault(digest,[]).append((p,f))
    def validate(pair):
        digest,items=pair;p,f=items[0]
        result=checked({'path':str(p),'sha256':digest},checks)
        kind=result.get('type','Otros'); group=GROUPS.get(kind,kind)
        if kind=='Formato_no_validado':group=EXTENSIONS.get(p.suffix.lower(),'No_identificados')
        status=result['status'];reason=result['reason']
        if f.get('Error') or 'Parcial' in f.get('State',''):
            if status.startswith('01_'):status='02_Con_avisos_o_danos'
            reason+=' La extracción reportó una incidencia: '+str(f.get('Error') or f['State'])
        return {'group':group,'status':status,'path':str(p),'sha256':digest,'source':str(p),
            'originalPath':f['OriginalName'] if f['Method']=='Metadatos' else '',
            'reason':reason,'identifiedExtension':result.get('identifiedExtension',p.suffix),
            'validation':result,'duplicateCount':len(items)-1,'duplicateSources':[str(q) for q,_ in items[1:]]}
    rows=[]
    with ThreadPoolExecutor(max_workers=3) as pool:
        for future in as_completed([pool.submit(validate,item) for item in unique.items()]):
            rows.append(future.result())
            if len(rows)%10==0 or len(rows)==len(unique):print(f'Validación estructural: {len(rows)}/{len(unique)}',flush=True)
    info=root/'informes';atomic_json(info/'por-tipo.json',{'files':rows})
    build(root,output)
    atomic_json(output/'validacion-resumen.json',{'uniqueFiles':len(rows),'duplicateCopies':sum(x['duplicateCount'] for x in rows),
        'unavailable':unavailable,'byStatus':dict(collections.Counter(x['status'] for x in rows)),
        'limit':'Una estructura válida no acredita equivalencia al original perdido. No es un análisis antivirus.'})

if __name__=='__main__':prepare(sys.argv[1],sys.argv[2] if len(sys.argv)>2 else None)
