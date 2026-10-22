import importlib.util, json, sqlite3, tempfile, threading, unittest, urllib.request, urllib.error, uuid
from pathlib import Path

spec=importlib.util.spec_from_file_location('feedback',Path(__file__).with_name('feedback.py'))
app=importlib.util.module_from_spec(spec);spec.loader.exec_module(app)

class BackendTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();app.STATE=Path(self.tmp.name);app.DB=app.STATE/'feedback.sqlite3'
        with sqlite3.connect(app.DB) as con:
            con.execute('CREATE TABLE feedback (id TEXT PRIMARY KEY,created TEXT NOT NULL,kind TEXT NOT NULL,message TEXT NOT NULL,version TEXT,email TEXT)')
            con.execute('INSERT INTO feedback VALUES (?,?,?,?,?,?)',('old','2026-01-01','error','Comentario anterior','1.0','private@example.invalid'))
        con.close()
        app.initialize();app.initialize()
        self.server=app.ThreadingHTTPServer(('127.0.0.1',0),app.Handler)
        self.thread=threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
    def tearDown(self):
        self.server.shutdown();self.server.server_close();self.tmp.cleanup()
    def request(self,path,data=None,host='127.0.0.1:18162',origin=None):
        headers={'Host':host}
        if origin:headers['Origin']=origin
        if data is not None:headers['Content-Type']='application/json'
        req=urllib.request.Request('http://127.0.0.1:'+str(self.server.server_port)+path,data=json.dumps(data).encode() if data is not None else None,headers=headers)
        try:
            with urllib.request.urlopen(req) as response:return response.status,response.read()
        except urllib.error.HTTPError as error:
            with error:return error.code,error.read()
    def test_migration_preserves_private_feedback(self):
        status,body=self.request('/admin/data');self.assertEqual(status,200)
        row=json.loads(body)['feedback'][0];self.assertEqual(row['id'],'old');self.assertEqual(row['status'],'nuevo');self.assertEqual(row['email'],'private@example.invalid')
    def test_public_cannot_read_or_modify_admin(self):
        self.assertEqual(self.request('/admin/data',host='revivo.digital')[0],404)
        self.assertEqual(self.request('/admin/update',{'id':'old','status':'resuelto','notes':''},origin='https://evil.invalid')[0],403)
        self.assertEqual(self.request('/admin/update',{'id':'old','status':'resuelto','notes':''})[0],403)
    def test_save_status_and_notes(self):
        self.assertEqual(self.request('/admin/update',{'id':'old','status':'resuelto','notes':'Ya revisado'},origin='http://127.0.0.1:18162')[0],200)
        row=json.loads(self.request('/admin/data')[1])['feedback'][0];self.assertEqual(row['notes'],'Ya revisado');self.assertEqual(row['status'],'resuelto')
    def test_counter_deduplicates_retries(self):
        event={'id':str(uuid.uuid4()),'kind':'download'}
        for _ in range(2):self.assertEqual(self.request('/api/events',event,origin='https://revivo.digital')[0],200)
        self.assertEqual(json.loads(self.request('/admin/data')[1])['totals']['download'],1)
        self.assertEqual(self.request('/api/events',{'id':'invalid','kind':'visit'},origin='https://revivo.digital')[0],400)
    def test_feedback_still_works_and_text_is_data(self):
        payload={'kind':'error','message':'<script>alert(1)</script>','email':'test@example.invalid'}
        self.assertEqual(self.request('/api/feedback',payload,origin='https://revivo.digital')[0],201)
        self.assertEqual(json.loads(self.request('/admin/data')[1])['total'],2)
        self.assertEqual(self.request('/api/feedback',payload,origin='https://evil.invalid')[0],403)
    def test_private_assets_and_pagination(self):
        self.assertEqual(self.request('/admin')[0],200);self.assertEqual(self.request('/admin/app.js')[0],200)
        self.assertEqual(self.request('/admin/data?page=invalid')[0],400)
        self.assertEqual(json.loads(self.request('/admin/data?page=1')[1])['feedback'],[])

if __name__=='__main__':unittest.main()
