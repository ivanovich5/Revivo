import json, os, re, sqlite3, uuid, shutil
from datetime import datetime, timezone
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path
from urllib.parse import urlsplit, parse_qs
STATE=Path(os.environ.get("STATE_DIRECTORY","/var/lib/revivo-feedback"))
DB=STATE/"feedback.sqlite3"
HERE=Path(__file__).parent
LOCAL_HOSTS={"127.0.0.1:18161","127.0.0.1:18162","localhost:18162"}
PUBLIC_ORIGINS={"https://revivo.digital","https://www.revivo.digital"}
class ClosingConnection(sqlite3.Connection):
    def __exit__(self,*args):
        try:return super().__exit__(*args)
        finally:self.close()
def connect():
    con=sqlite3.connect(DB,timeout=10,factory=ClosingConnection);con.row_factory=sqlite3.Row;return con
def initialize():
    with connect() as con:
        con.execute("CREATE TABLE IF NOT EXISTS feedback (id TEXT PRIMARY KEY, created TEXT NOT NULL, kind TEXT NOT NULL, message TEXT NOT NULL, version TEXT, email TEXT)")
        columns={r[1] for r in con.execute("PRAGMA table_info(feedback)")}
        if "status" not in columns:con.execute("ALTER TABLE feedback ADD COLUMN status TEXT NOT NULL DEFAULT 'nuevo'")
        if "notes" not in columns:con.execute("ALTER TABLE feedback ADD COLUMN notes TEXT NOT NULL DEFAULT ''")
        con.execute("CREATE TABLE IF NOT EXISTS daily (day TEXT NOT NULL, kind TEXT NOT NULL, count INTEGER NOT NULL, PRIMARY KEY(day,kind))")
        con.execute("CREATE TABLE IF NOT EXISTS recent_events (id TEXT PRIMARY KEY, created TEXT NOT NULL)")
        con.execute("CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
        con.execute("INSERT OR IGNORE INTO metadata VALUES (?,?)",("started",datetime.now(timezone.utc).isoformat()))
class Handler(BaseHTTPRequestHandler):
    def setup(self):
        super().setup();self.connection.settimeout(10)
    def log_message(self,*args):pass
    def reply(self,code,body,mime="application/json; charset=utf-8"):
        data=json.dumps(body,ensure_ascii=False).encode("utf-8") if isinstance(body,(dict,list)) else body
        self.send_response(code);self.send_header("Content-Type",mime);self.send_header("Content-Length",str(len(data)))
        self.send_header("Cache-Control","no-store");self.send_header("X-Content-Type-Options","nosniff")
        self.send_header("Content-Security-Policy","default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'")
        self.send_header("Referrer-Policy","no-referrer");self.end_headers();self.wfile.write(data)
    def private(self):
        return self.headers.get("Host") in LOCAL_HOSTS and not self.headers.get("X-Forwarded-For")
    def do_GET(self):
        path=urlsplit(self.path).path
        if not path.startswith("/admin") or not self.private():return self.reply(404,{"ok":False})
        assets={"/admin":("admin.html","text/html; charset=utf-8"),"/admin/":("admin.html","text/html; charset=utf-8"),"/admin/app.js":("admin.js","text/javascript; charset=utf-8"),"/admin/style.css":("admin.css","text/css; charset=utf-8")}
        if path in assets:
            name,mime=assets[path];return self.reply(200,(HERE/name).read_bytes(),mime)
        if path!="/admin/data":return self.reply(404,{"ok":False})
        try:page=max(0,min(100000,int(parse_qs(urlsplit(self.path).query).get("page",["0"])[0])))
        except ValueError:return self.reply(400,{"ok":False})
        with connect() as con:
            rows=[dict(r) for r in con.execute("SELECT * FROM feedback ORDER BY created DESC LIMIT 50 OFFSET ?",(page*50,))]
            totals={r["kind"]:r["n"] for r in con.execute("SELECT kind,SUM(count) n FROM daily GROUP BY kind")}
            days=[dict(r) for r in con.execute("SELECT * FROM daily WHERE day >= date('now','-29 days') ORDER BY day DESC,kind")]
            statuses={r["status"]:r["n"] for r in con.execute("SELECT status,COUNT(*) n FROM feedback GROUP BY status")}
            started=con.execute("SELECT value FROM metadata WHERE key='started'").fetchone()[0]
        try:maintenance=json.loads((STATE/"maintenance.json").read_text())
        except (OSError,ValueError):maintenance={"error":"Todavía no hay un informe de mantenimiento."}
        disk=shutil.disk_usage(STATE)
        self.reply(200,{"feedback":rows,"page":page,"total":sum(statuses.values()),"statuses":statuses,"totals":totals,"days":days,"started":started,"maintenance":maintenance,"disk":{"free":disk.free,"total":disk.total}})
    def do_POST(self):
        admin=self.path=="/admin/update"
        if self.path not in ("/api/feedback","/api/events","/admin/update"):return self.reply(404,{"ok":False})
        origin=self.headers.get("Origin")
        if admin:
            if not self.private() or origin!="http://"+self.headers.get("Host",""):return self.reply(403,{"ok":False})
        elif origin not in PUBLIC_ORIGINS:return self.reply(403,{"ok":False})
        if self.headers.get("Content-Type","").split(";")[0]!="application/json":return self.reply(415,{"ok":False})
        try:
            length=int(self.headers.get("Content-Length","0"))
            if not 1<=length<=8192:return self.reply(413,{"ok":False})
            data=json.loads(self.rfile.read(length))
            if not isinstance(data,dict):raise ValueError()
            if admin:
                if data.get("status") not in ("nuevo","en_revision","resuelto") or not isinstance(data.get("notes"),str) or len(data["notes"])>2000 or not isinstance(data.get("id"),str):raise ValueError()
                with connect() as con:
                    result=con.execute("UPDATE feedback SET status=?,notes=? WHERE id=?",(data["status"],data["notes"],data["id"]))
                    if result.rowcount!=1:return self.reply(404,{"ok":False})
            elif self.path=="/api/events":
                if data.get("kind") not in ("visit","download") or not isinstance(data.get("id"),str) or not re.fullmatch(r"[a-f0-9-]{36}",data["id"]):raise ValueError()
                now=datetime.now(timezone.utc)
                with connect() as con:
                    con.execute("DELETE FROM recent_events WHERE created < datetime('now','-2 days')")
                    inserted=con.execute("INSERT OR IGNORE INTO recent_events VALUES (?,?)",(data["id"],now.strftime("%Y-%m-%d %H:%M:%S"))).rowcount
                    if inserted:con.execute("INSERT INTO daily VALUES (?,?,1) ON CONFLICT(day,kind) DO UPDATE SET count=count+1",(now.date().isoformat(),data["kind"]))
            else:
                if any(not isinstance(data.get(k,""),str) for k in ("kind","message","version","email","website")):raise ValueError()
                if data.get("website"):return self.reply(200,{"ok":True})
                kind,message,version,email=(data.get(k,"").strip() for k in ("kind","message","version","email"))
                if kind not in ("error","suggestion","experience") or not 10<=len(message)<=4000 or len(version)>40 or len(email)>254:raise ValueError()
                if email and not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+",email):raise ValueError()
                with connect() as con:con.execute("INSERT INTO feedback (id,created,kind,message,version,email) VALUES (?,?,?,?,?,?)",(str(uuid.uuid4()),datetime.now(timezone.utc).isoformat(),kind,message,version,email))
        except (ValueError,UnicodeError,TimeoutError):return self.reply(400,{"ok":False})
        except sqlite3.Error:return self.reply(503,{"ok":False})
        self.reply(200 if admin or self.path=="/api/events" else 201,{"ok":True})
if __name__=="__main__":
    initialize()
    ThreadingHTTPServer(("127.0.0.1",18161),Handler).serve_forever()
