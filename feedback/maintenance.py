#!/usr/bin/env python3
"""Private, scoped REVIVO backup and health check. Run as root via systemd."""
import hashlib, json, os, shutil, sqlite3, subprocess, tarfile, tempfile, urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT=Path('/var/www/revivo.digital/dist')
STATE=Path('/var/lib/revivo-feedback')
BACKUPS=Path('/var/backups/revivo/daily')

def now():return datetime.now(timezone.utc).isoformat()

def run():
    os.umask(0o077)
    BACKUPS.mkdir(parents=True,exist_ok=True)
    report={'checked':now(),'checks':{},'warnings':[]}
    try:report['offsite']=(STATE/'offsite-confirmed.txt').read_text().strip()
    except OSError:pass
    disk=shutil.disk_usage(ROOT)
    if disk.free/disk.total<.15:report['warnings'].append('Queda menos del 15 % de espacio. Revisa el almacenamiento del servidor.')
    if disk.free<512*1024*1024:raise RuntimeError('Espacio insuficiente para crear un respaldo con seguridad.')
    with tempfile.TemporaryDirectory(prefix='revivo-backup-') as tmp:
        folder=Path(tmp);copy=folder/'feedback.sqlite3'
        with sqlite3.connect('file:'+str(STATE/'feedback.sqlite3')+'?mode=ro',uri=True) as source, sqlite3.connect(copy) as dest:
            source.backup(dest)
            if dest.execute('PRAGMA integrity_check').fetchone()[0]!='ok':raise RuntimeError('La base de datos requiere revisión.')
            count=dest.execute('SELECT COUNT(*) FROM feedback').fetchone()[0]
        source.close();dest.close()
        manifest=[]
        for path in sorted((ROOT/'descargas').rglob('*')):
            if path.is_file():
                with path.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
                manifest.append(digest+'  '+str(path.relative_to(ROOT)))
        (folder/'downloads-sha256.txt').write_text('\n'.join(manifest)+'\n')
        stamp=datetime.now(timezone.utc).strftime('%Y-%m-%d')
        final=BACKUPS/f'revivo-{stamp}.tar.gz';pending=final.with_suffix('.partial')
        with tarfile.open(pending,'w:gz') as tar:
            tar.add(copy,arcname='feedback.sqlite3')
            tar.add(folder/'downloads-sha256.txt',arcname='downloads-sha256.txt')
            for name in ('index.html','main.js','style.css','config.js','assets'):
                tar.add(ROOT/name,arcname='web/'+name)
            tar.add('/opt/revivo-feedback',arcname='server',filter=lambda info:None if '__pycache__' in info.name else info)
            for path in ('/etc/nginx/sites-available/revivo-digital','/etc/nginx/conf.d/revivo-feedback-limit.conf','/etc/nginx/conf.d/revivo-events-limit.conf','/etc/systemd/system/revivo-feedback.service','/etc/systemd/system/revivo-maintenance.service','/etc/systemd/system/revivo-maintenance.timer'):
                tar.add(path,arcname='configuration/'+Path(path).name)
        # Validate restoration of the actual archived database without modifying production.
        with tarfile.open(pending) as tar:
            tar.getmembers()
            with tar.extractfile('feedback.sqlite3') as src, (folder/'restored.sqlite3').open('wb') as out:shutil.copyfileobj(src,out)
        with sqlite3.connect(folder/'restored.sqlite3') as restored:
            assert restored.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
            assert restored.execute('SELECT COUNT(*) FROM feedback').fetchone()[0]==count
        restored.close()
        pending.replace(final)
        with final.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
        final.with_suffix(final.suffix+'.sha256').write_text(digest+'  '+final.name+'\n')
        report['backup']=now();report['checks']['Respaldo y prueba de restauración']=True
        # Retain only this tool's dated backup files; never touch other server data.
        for old in sorted(BACKUPS.glob('revivo-????-??-??.tar.gz'))[:-7]:
            old.unlink();old.with_suffix(old.suffix+'.sha256').unlink(missing_ok=True)
    for unit in ('revivo-feedback','nginx'):
        report['checks'][unit]=subprocess.run(['systemctl','is-active','--quiet',unit]).returncode==0
    report['checks']['Renovación automática HTTPS']=subprocess.run(['systemctl','is-active','--quiet','certbot.timer']).returncode==0
    report['checks']['Certificado HTTPS vigente por más de 14 días']=subprocess.run(['openssl','x509','-checkend','1209600','-noout','-in','/etc/letsencrypt/live/revivo.digital/fullchain.pem'],stdout=subprocess.DEVNULL).returncode==0
    for label,url in [('Página HTTPS','https://revivo.digital/'),('Descarga pública','https://revivo.digital/descargas/1.0.2/REVIVO-USB-1.0.2-setup.exe')]:
        try:
            with urllib.request.urlopen(urllib.request.Request(url,method='HEAD'),timeout=15) as response:report['checks'][label]=response.status==200
        except Exception:report['checks'][label]=False
    report['checks']['Rotación de registros']=Path('/etc/logrotate.d/nginx').exists()
    save(report)

def save(report):
    pending=STATE/'maintenance.json.new'
    pending.write_text(json.dumps(report,ensure_ascii=False),encoding='utf-8');pending.chmod(0o644)
    pending.replace(STATE/'maintenance.json')

if __name__=='__main__':
    try:run()
    except Exception as error:
        save({'checked':now(),'error':'Falló el mantenimiento: '+str(error),'warnings':['Revisa el respaldo antes de confiar en él.']})
        raise
