import json, os, re, sqlite3, uuid
from datetime import datetime, timezone
from http.server import HTTPServer, BaseHTTPRequestHandler
from pathlib import Path
DB=Path(os.environ.get("STATE_DIRECTORY","/var/lib/revivo-feedback"))/"feedback.sqlite3"
def connect():
    con=sqlite3.connect(DB,timeout=5)
    con.execute("CREATE TABLE IF NOT EXISTS feedback (id TEXT PRIMARY KEY, created TEXT NOT NULL, kind TEXT NOT NULL, message TEXT NOT NULL, version TEXT, email TEXT)")
    return con
with connect() as con: pass
class Handler(BaseHTTPRequestHandler):
    def setup(self):
        super().setup();self.connection.settimeout(10)
    def log_message(self,*args): pass
    def reply(self,code,body):
        data=json.dumps(body).encode();self.send_response(code);self.send_header("Content-Type","application/json; charset=utf-8");self.send_header("Content-Length",str(len(data)));self.send_header("Cache-Control","no-store");self.end_headers();self.wfile.write(data)
    def do_GET(self): self.reply(405,{"ok":False})
    def do_POST(self):
        if self.path!="/api/feedback": return self.reply(404,{"ok":False})
        if self.headers.get("Origin") not in ("https://revivo.digital","https://www.revivo.digital"): return self.reply(403,{"ok":False})
        if self.headers.get("Content-Type","").split(";")[0]!="application/json":return self.reply(415,{"ok":False})
        try:
            length=int(self.headers.get("Content-Length","0"))
            if not 1<=length<=8192:return self.reply(413,{"ok":False})
            data=json.loads(self.rfile.read(length))
            if not isinstance(data,dict):raise ValueError()
            if any(not isinstance(data.get(k,""),str) for k in ("kind","message","version","email","website")):raise ValueError()
            if data.get("website"):return self.reply(200,{"ok":True})
            kind=data.get("kind","");message=data.get("message","").strip();version=data.get("version","").strip();email=data.get("email","").strip()
            if kind not in ("error","suggestion","experience") or not 10<=len(message)<=4000 or len(version)>40 or len(email)>254:raise ValueError()
            if email and not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+",email):raise ValueError()
        except (ValueError,UnicodeError,TimeoutError):return self.reply(400,{"ok":False})
        try:
            with connect() as con:con.execute("INSERT INTO feedback VALUES (?,?,?,?,?,?)",(str(uuid.uuid4()),datetime.now(timezone.utc).isoformat(),kind,message,version,email))
        except sqlite3.Error:return self.reply(503,{"ok":False})
        self.reply(201,{"ok":True})
HTTPServer(("127.0.0.1",18161),Handler).serve_forever()
