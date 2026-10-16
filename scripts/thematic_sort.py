"""Conservative local thematic grouping. Never substitutes for file validation."""
from concurrent.futures import ThreadPoolExecutor,as_completed
import collections
import csv
import json
from pathlib import Path
import re
import subprocess
import sys
import unicodedata
import rigorous_review as review

TERMS={
 'Oncologia':['cancer','tumor','tumores','carcinoma','metastasis','quimioterapia','oncologia','neoplasia','oncology','chemotherapy','cancerous'],
 'Pediatria':['pediatria','pediatrico','pediatricos','lactante','lactantes','neonato','neonatal','pediatrics','pediatric','infants'],
 'Ginecologia_y_obstetricia':['embarazo','embarazada','gestacion','obstetricia','parto','gestante','preeclampsia','materna','feto','pregnancy','obstetric'],
 'Cardiologia':['cardiaco','cardiaca','cardiologia','miocardio','coronario','arritmia','cardiovascular','cardiac','myocardial','cardiology'],
 'Neumologia':['pulmonar','pulmon','neumonia','respiratoria','respiratorio','asma','epoc','tromboembolia','pulmonary','respiratory'],
 'Infecciones_y_vacunas':['vacuna','vacunas','vacunacion','inmunizacion','antibiotico','antibioticos','bacteriana','infectologia','vaccine','vaccines','vaccination'],
 'Endocrinologia_y_metabolismo':['diabetes','insulina','glucosa','hipoglucemia','endocrinologia','tiroides','diabetico','hipotiroidismo','insulin','glucose'],
 'Nefrologia':['renal','renales','nefrologia','creatinina','dialisis','glomerular','hemodialisis','nephrology','kidney'],
 'Neurologia':['neurologia','neurona','neuronas','cerebral','epilepsia','convulsion','convulsiones','neurology','seizure'],
 'Cirugia_y_traumatologia':['cirugia','quirurgico','quirurgica','fractura','fracturas','traumatologia','postoperatorio','surgery','surgical'],
 'Gastroenterologia':['gastroenterologia','hepatico','hepatica','higado','intestinal','cirrosis','pancreatitis','hepatitis','gastritis'],
}

def classify(text):
    s=''.join(c for c in unicodedata.normalize('NFKD',text.lower()) if not unicodedata.combining(c))
    counts=collections.Counter(re.findall('[a-z]{3,}',s));scores=[]
    for label,terms in TERMS.items():
        hit=[counts[w] for w in terms if counts[w]]
        scores.append((sum(min(n,12) for n in hit),len(hit),label))
    scores.sort(reverse=True);top=scores[0]
    if top[0]>=10 and top[1]>=3 and top[0]>=scores[1][0]*1.5:
        return {'subtopic':top[2],'subtopicScore':top[0],'distinctTerms':top[1]}
    return {'subtopic':'General_o_tema_mixto'}

def worker(source,out):
    result={'subtopic':'General_o_tema_mixto'}
    try:
        p=Path(source); text=''
        with p.open('rb') as f:head=f.read(8)
        if head.startswith(b'%PDF-'):
            import fitz
            fitz.TOOLS.mupdf_display_errors(False);fitz.TOOLS.mupdf_display_warnings(False)
            with fitz.open(p) as doc:
                if not doc.needs_pass:
                    for n in range(min(8,doc.page_count)):
                        text+=doc[n].get_text()[:80000-len(text)]+'\n'
                        if len(text)>=80000:break
        elif p.suffix.lower()=='.txt':
            with p.open('rb') as f:b=f.read(80000)
            text=b.decode('utf-8',errors='replace')
        result={**classify(text),'basis':'Reglas locales; hasta 8 primeras páginas o 80 mil caracteres; sin revisión humana'}
    except Exception:result['basis']='No se pudo obtener texto suficiente'
    review.atomic_json(out,result)

def one(item,cache):
    out=cache/(item['sha256']+'.json')
    if not out.exists():
        try:subprocess.run([sys.executable,__file__,'--worker',item['path'],str(out)],timeout=8,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=subprocess.CREATE_NO_WINDOW)
        except Exception:review.atomic_json(out,{'subtopic':'General_o_tema_mixto','basis':'Proceso limitado o sin texto suficiente'})
    return item['sha256'],json.loads(out.read_text(encoding='utf-8'))

def run(job):
    job=Path(job).resolve();root=Path(json.loads((job/'ultima-revision-rigurosa.json').read_text(encoding='utf-8'))['output']).resolve()
    if not root.is_relative_to(job):raise ValueError('Destino fuera del caso')
    manifest=root/'informes'/'manifest.json';data=json.loads(manifest.read_text(encoding='utf-8'))
    items=[r for r in data['files'] if r.get('topic')=='Salud' and r.get('organizedPath')]
    cache=root/'informes'/'temas';cache.mkdir(exist_ok=True);classified={}
    with ThreadPoolExecutor(max_workers=4) as pool:
        for n,fut in enumerate(as_completed([pool.submit(one,x,cache) for x in items]),1):
            digest,result=fut.result();classified[digest]=result
            if n%100==0:print(f'Temas revisados {n}/{len(items)}',flush=True)
    for item in items:
        result=classified[item['sha256']];source=Path(item['organizedPath']).resolve()
        destination=source.parent/result['subtopic']/source.name
        if not source.is_relative_to(root) or not destination.resolve().is_relative_to(root):raise ValueError('Ruta fuera del resultado')
        destination.parent.mkdir(exist_ok=True)
        if destination.exists():raise FileExistsError('No se sobrescriben resultados')
        source.rename(destination)
        item.update(result);item['organizedPath']=str(destination)
    data['summary']['medicalSubtopics']=dict(collections.Counter(x['subtopic'] for x in classified.values()))
    review.atomic_json(manifest,data)
    with (root/'informes'/'catalogo.csv').open('w',encoding='utf-8-sig',newline='') as f:
        fields=['path','organizedPath','size','sha256','status','type','topic','subtopic','reason','pages','pagesChecked','copySha256Verified']
        writer=csv.DictWriter(f,fieldnames=fields,extrasaction='ignore');writer.writeheader();writer.writerows(data['files'])
    for p in [root/'resumen-final.json',job/'ultima-revision-rigurosa.json']:
        s=json.loads(p.read_text(encoding='utf-8'));s['medicalSubtopics']=data['summary']['medicalSubtopics'];review.atomic_json(p,s)
    print(json.dumps(data['summary']['medicalSubtopics'],ensure_ascii=False,indent=2))

if __name__=='__main__':
    if sys.argv[1]=='--worker':worker(sys.argv[2],sys.argv[3])
    else:run(sys.argv[1])
