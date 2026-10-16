import json,sys,tempfile,unittest,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
import rigorous_review as rr
import preview_export as pe

class Validation(unittest.TestCase):
    def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
    def tearDown(self):self.tmp.cleanup()
    def office(self,broken=False):
        p=self.root/'document.docx'
        with zipfile.ZipFile(p,'w') as z:
            z.writestr('word/document.xml','<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Documento de prueba</w:t></w:r></w:p></w:body></w:document>')
            z.writestr('[Content_Types].xml','<Types><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
            z.writestr('_rels/.rels','<Relationships><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
            if broken:z.writestr('word/_rels/document.xml.rels','<Relationships><Relationship Id="r2" Target="media/missing.png"/></Relationships>')
        return p
    def test_office_relationships(self):
        self.assertEqual(rr.inspect_file(self.office())['status'],'01_Comprobacion_superada')
        with self.assertRaises(ValueError):rr.inspect_file(self.office(True))
    def test_pdf_word_alone_not_fragment(self):
        p=self.root/'notes.txt';p.write_text('El marcador endobj se usa en PDF.',encoding='utf-8')
        self.assertEqual(rr.inspect_file(p)['type'],'Texto_legible')
    def test_pipeline_validates_groups_duplicates_and_preserves_originals(self):
        import fitz
        doc=self.office();pdf=self.root/'wrong.txt'
        with fitz.open() as d:
            for n in range(3):d.new_page().insert_text((30,30),f'Page {n+1}')
            d.save(pdf)
        fragment=self.root/'fragment.txt';fragment.write_bytes(b'21 0 obj\n<< /Type /Font >>\nendobj')
        damaged=self.root/'bad.pdf';damaged.write_bytes(b'%PDF-1.4\nbroken')
        duplicate=self.root/'duplicate.txt';duplicate.write_bytes(fragment.read_bytes())
        files=[doc,pdf,fragment,damaged,duplicate];before={p:rr.sha(p) for p in files}
        report={'Files':[{'OutputName':p.name,'OriginalName':p.name,'Sha256':before[p],'Method':'Firmas','State':'Extraído','Error':None} for p in files]}
        (self.root/'informe.json').write_text(json.dumps(report),encoding='utf-8')
        pe.prepare(self.root)
        rows=json.loads((self.root/'vistas-integradas/catalog.json').read_text(encoding='utf-8'))
        byname={x['name']:x for x in rows}
        self.assertEqual(byname['wrong.txt']['group'],'PDF');self.assertEqual(byname['wrong.txt']['identifiedExtension'],'.pdf')
        self.assertEqual(len(byname['wrong.txt']['images']),3)
        self.assertEqual(byname['document.docx']['status'],'01_Comprobacion_superada')
        self.assertEqual(byname['fragment.txt']['group'],'Fragmentos_PDF');self.assertEqual(byname['fragment.txt']['duplicateCount'],1)
        self.assertEqual(byname['bad.pdf']['status'],'02_Con_avisos_o_danos')
        self.assertEqual(len(rows),4);self.assertEqual(before,{p:rr.sha(p) for p in files})
    def test_changed_file_not_certified(self):
        p=self.root/'changed.txt';p.write_bytes(b'original');digest=rr.sha(p);p.write_bytes(b'changed')
        checks=self.root/'checks';checks.mkdir()
        result=rr.checked({'path':str(p),'sha256':digest},checks)
        self.assertEqual(result['type'],'Cambio_detectado');self.assertEqual(result['status'],'04_Sin_validar')

if __name__=='__main__':unittest.main()
