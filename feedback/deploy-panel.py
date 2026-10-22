"""Run on the existing REVIVO host after copying server files."""
from pathlib import Path
import subprocess

site=Path('/etc/nginx/sites-available/revivo-digital')
config=site.read_text()
marker='    location = /api/feedback {'
if 'location = /api/events' not in config:
    config=config.replace(marker,'''    location = /api/events {
        client_max_body_size 1k;
        limit_req zone=revivo_events burst=10 nodelay;
        limit_req_status 429;
        proxy_pass http://127.0.0.1:18161;
        proxy_connect_timeout 3s;
        proxy_read_timeout 15s;
    }
    location ^~ /admin { return 404; }
'''+marker)
Path('/etc/nginx/conf.d/revivo-events-limit.conf').write_text('limit_req_zone $binary_remote_addr zone=revivo_events:1m rate=30r/m;\n')
site.write_text(config)
subprocess.run(['nginx','-t'],check=True)
Path('/var/backups/revivo').mkdir(mode=0o700,exist_ok=True)
subprocess.run(['systemctl','daemon-reload'],check=True)
subprocess.run(['systemctl','restart','revivo-feedback'],check=True)
subprocess.run(['systemctl','reload','nginx'],check=True)
subprocess.run(['systemctl','enable','--now','revivo-maintenance.timer'],check=True)
