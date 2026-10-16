"""Create an independent, deduplicated type-first view. Originals stay untouched."""
import csv
import json
from pathlib import Path
import shutil
import sys
import collections
import rigorous_review as rr
from recover_fat_evidence import clean

GROUPS={'PDF':'PDF','Diseno_Illustrator_PDF':'Diseno_Illustrator','Excel':'XLSX','Word':'DOCX','PowerPoint':'PPTX',
        'Imagenes':'Imagenes','Audio_WAV':'Audio_WAV','Texto_legible':'Texto_sin_confirmar','Texto_XML_HTML':'Texto_sin_confirmar',
        'Metadatos_Apple':'Metadatos_Apple','Office_antiguo_u_OLE':'Office_antiguo_sin_validar','Comprimidos_RAR':'Comprimidos_RAR','ZIP':'Comprimidos_ZIP','Libros_EPUB':'Libros_EPUB'}

def run(job):
    job=Path(job).resolve();summary=json.loads((job/'ultima-revision-rigurosa.json').read_text(encoding='utf-8'));root=Path(summary['output']).resolve()
    if not root.is_relative_to(job):raise ValueError('Output outside case')
    destroot=root/'00_Por_tipo';destroot.mkdir(exist_ok=False)
    unique={}
    for name in ['manifest.json','manifest-metadatos.json']:
        for row in json.loads((root/'informes'/name).read_text(encoding='utf-8'))['files']:
            if row.get('organizedPath'):unique[row['sha256']]=row
    records=[]
    for digest,row in unique.items():
        source=Path(row['organizedPath']).resolve()
        if not source.is_relative_to(root) or source.is_symlink():raise ValueError('Unsafe source')
        group='Fragmentos' if row['status']=='03_Fragmentos' else GROUPS.get(row['type'],'No_identificados')
        directory=destroot/group/row['status'];directory.mkdir(parents=True,exist_ok=True)
        name=clean(Path(row.get('originalPath',source.name)).name)
        filename=Path(name);destination=directory/(filename.stem[:80]+'__'+digest[:12]+filename.suffix)
        if not destination.resolve().is_relative_to(destroot):raise ValueError('Unsafe destination')
        with source.open('rb') as s,destination.open('xb') as d:shutil.copyfileobj(s,d,1024*1024)
        if rr.sha(destination)!=digest:raise IOError('Copy hash mismatch')
        records.append({'group':group,'status':row['status'],'path':str(destination),'sha256':digest,'source':str(source),'originalPath':row.get('originalPath',''),'reason':row['reason']})
        if len(records)%200==0:print(f'Agrupados y verificados {len(records)}/{len(unique)}',flush=True)
    rr.atomic_json(root/'informes'/'por-tipo.json',{'files':records,'uniqueFiles':len(records),'groups':dict(collections.Counter(r['group'] for r in records))})
    with (destroot/'catalogo.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=list(records[0]));w.writeheader();w.writerows(records)
    (destroot/'LEEME.txt').write_text('ARCHIVOS AGRUPADOS POR TIPO\n\nCada contenido aparece una sola vez en esta vista.\nDentro de cada tipo, revisa primero 01_Comprobacion_superada.\n02_Con_avisos_o_danos puede contener documentos parcialmente útiles.\n04_Sin_validar no acredita integridad. Fragmentos no son documentos completos.\nNo se asigna el tipo solamente por la extensión.\n\nLas carpetas originales parciales y la agrupación temática siguen disponibles en la carpeta superior.\nLas copias de esta vista son independientes de los resultados anteriores.\n',encoding='utf-8')
    print(json.dumps({'path':str(destroot),'files':len(records),'groups':dict(collections.Counter(r['group'] for r in records))},ensure_ascii=False,indent=2))

if __name__=='__main__':run(sys.argv[1])
