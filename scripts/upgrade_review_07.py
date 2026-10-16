import concurrent.futures,json,subprocess,sys
from pathlib import Path
from build_review_browser import process

def upgrade(catalog):
    path=Path(catalog); rows=json.loads(path.read_text(encoding='utf-8')); backup=path.with_name('catalog-v06.json')
    if not backup.exists():backup.write_bytes(path.read_bytes())
    def one(row):
        if row['group'] not in ('XLSX','Texto_sin_confirmar'):return row
        dest=path.parent/'vistas-07'/row['sha256'];dest.mkdir(parents=True,exist_ok=True)
        request=dest/'request.json';request.write_text(json.dumps(row),encoding='utf-8')
        try:
            p=subprocess.run([sys.executable,str(Path(__file__).with_name('build_review_browser.py')),'--worker',str(request),str(dest)],capture_output=True,timeout=30,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            if p.returncode:raise ValueError(p.stderr.decode(errors='replace')[-200:])
            data=json.loads((dest/'result.json').read_text(encoding='utf-8'));data['images']=[str(dest/n) for n in data['images']]
            return {**row,**data}
        except Exception as e:return {**row,'note':'Vista no disponible: '+str(e)}
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool: result=list(pool.map(one,rows))
    temp=path.with_suffix('.tmp');temp.write_text(json.dumps(result,ensure_ascii=False),encoding='utf-8');temp.replace(path)
    print(json.dumps({'files':len(result),'spreadsheets':sum(bool(r.get('sheets')) for r in result),'textPreviews':sum(r['group']=='Texto_sin_confirmar' and bool(r['text']) for r in result),'errors':sum(r['note'].startswith('Vista no disponible') for r in result)}))
if __name__=='__main__':upgrade(sys.argv[1])
