"""Validate metadata recovery, retain partial original paths, and publish evidence.
Content is never launched. Only a newly generated results tree is modified.
"""
from concurrent.futures import ThreadPoolExecutor,as_completed
import collections
import csv
import json
from pathlib import Path
import re
import shutil
import sys
import rigorous_review as review
from recover_fat_evidence import clean

def run(job):
    job=Path(job).resolve()
    org=json.loads((job/'ultima-organizacion.json').read_text(encoding='utf-8'))
    root=Path(org['output']).resolve()
    if not root.is_relative_to(job) or root.is_symlink():raise ValueError('Destino fuera del caso')
    old=json.loads((root/'informes'/'manifest.json').read_text(encoding='utf-8'))
    loc=json.loads((job/'ultima-reconstruccion.json').read_text(encoding='utf-8'))
    meta=json.loads(Path(loc['manifest']).read_text(encoding='utf-8'))
    extracted=[r for r in meta['files'] if 'path' in r]
    cache=root/'informes'/'validaciones'
    unique={r['sha256']:{'path':r['path'],'size':r['size'],'sha256':r['sha256']} for r in extracted}
    verified={}
    with ThreadPoolExecutor(max_workers=4) as pool:
        pending=[pool.submit(review.checked,x,cache) for x in unique.values()]
        for fut in as_completed(pending):
            result=fut.result();verified[result['sha256']]=result
            if len(verified)%10==0:print(f'Revisión por metadatos {len(verified)}/{len(unique)}',flush=True)
    original_copies=[]
    for e in extracted:
        check=verified[e['sha256']]
        if check['status']=='06_No_copiar_contenido_activo' or check['type']=='Cambio_detectado':continue
        parts=[clean(p) for p in e['originalPath'].split('/')]
        destination=root/'00_Carpetas_originales_parciales'/check['status']
        for part in parts[:-1]:destination/=part
        destination/=parts[-1]
        if not destination.resolve().is_relative_to(root):raise ValueError('Destino no seguro')
        destination.parent.mkdir(parents=True,exist_ok=True)
        if destination.exists():destination=destination.with_name(destination.stem+'__'+e['sha256'][:12]+destination.suffix)
        with Path(e['path']).open('rb') as source,destination.open('xb') as out:shutil.copyfileobj(source,out,1024*1024)
        if review.sha(destination)!=e['sha256']:raise ValueError('Copia original no coincide')
        original_copies.append({**e,**check,'organizedPath':str(destination),'copySha256Verified':True})
    # Candidate name associations are an index only: no guessed path is promoted.
    inv=json.loads((job/'inventario-recuperado.json').read_text(encoding='utf-8'))
    starts=collections.defaultdict(list)
    for item in inv['files']:
        m=re.match(r'f(\d+)',Path(item['path']).name)
        if m:starts[int(m[1])*512].append(item)
    candidates=[]
    for e in meta['files']:
        for carved in starts.get(e.get('startByte'),[]):
            candidates.append({'candidateOriginalPath':e['originalPath'],'rootConnected':e['rootConnected'],
              'carvedPath':carved['path'],'metadataSize':e['size'],'carvedSize':carved['size'],
              'sameSize':e['size']==carved['size'],'offset':e['startByte'],
              'note':'Asociación por sector inicial y tamaño; cadena no confirmada salvo extracción FAT documentada. No prueba integridad ni identidad.'})
    review.atomic_json(root/'informes'/'nombres-candidatos.json',candidates)
    fields=['candidateOriginalPath','rootConnected','carvedPath','metadataSize','carvedSize','sameSize','offset','note']
    with (root/'informes'/'nombres-candidatos.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(candidates)
    # Preserve raw directory evidence, including entries not recoverable as files.
    for source,name in [(job/'evidencias-directorios.json','evidencias-directorios.json'),(Path(loc['manifest']),'reconstruccion-directorios.json')]:
        with source.open('rb') as s,(root/'informes'/name).open('xb') as d:shutil.copyfileobj(s,d)
    hashes_old={r['sha256'] for r in old['files']}; hashes_meta=set(verified)
    combined={r['sha256']:r for r in old['files']}
    combined.update(verified)
    summary={**org,'metadataDirectoriesRead':len(meta['directories']),
      'rootConnectedDirectories':sum(r['rootConnected'] for r in meta['directories']),
      'metadataFileEntries':len(meta['files']),'metadataFilesExported':len(original_copies),
      'metadataConnectedToRootFiles':sum(r['rootConnected'] for r in original_copies),
      'metadataNewUniqueContents':len(hashes_meta-hashes_old),
      'metadataValidation':dict(collections.Counter(r['status'] for r in original_copies)),
      'allUniqueContentsIncludingExcluded':len(combined),
      'allUniqueByStatus':dict(collections.Counter(r['status'] for r in combined.values())),
      'organizedPhysicalCopies':org['organizedFiles']+len(original_copies),
      'candidateNameAssociations':len(candidates),'candidateNameAssociationsSameSize':sum(r['sameSize'] for r in candidates),
      'originalPathPolicy':'Sólo archivos extraídos mediante cadenas FAT completas concordantes en ambas copias; estado de contenido separado. Ramas sin raíz identificadas explícitamente.',
      'topicPolicy':'Reglas locales por palabras; categorías orientativas, no carpetas originales.'}
    review.atomic_json(root/'informes'/'manifest-metadatos.json',{'summary':summary,'files':original_copies})
    review.atomic_json(root/'resumen-final.json',summary);review.atomic_json(job/'ultima-revision-rigurosa.json',summary)
    print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)

if __name__=='__main__':run(sys.argv[1])
