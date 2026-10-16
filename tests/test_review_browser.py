import json,sys,tempfile,unittest,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
import build_review_browser as b

class Checks(unittest.TestCase):
    def setUp(self): self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
    def tearDown(self): self.tmp.cleanup()
    def item(self,p,group,status='02_Con_avisos_o_danos'):
        return {'path':str(p),'sha256':b.digest(p),'group':group,'status':status}
    def test_hash_change_blocks_preview(self):
        p=self.root/'x.pdf';p.write_bytes(b'changed');item=self.item(p,'PDF');p.write_bytes(b'changed again')
        with self.assertRaises(ValueError):b.process(item,self.root/'out')
    def test_partial_pdf_preserves_source_and_limits_pages(self):
        import fitz
        p=self.root/'x.pdf'
        with fitz.open() as d:
            for i in range(22):d.new_page().insert_text((30,30),f'Page {i+1}')
            d.save(p)
        before=b.digest(p);dest=self.root/'out';b.process(self.item(p,'PDF'),dest)
        r=json.loads((dest/'result.json').read_text());self.assertTrue(r['partial']);self.assertEqual(r['pagesTotal'],22);self.assertEqual(r['pagesSaved'],20)
        self.assertEqual(before,b.digest(p));self.assertTrue((dest/'texto-parcial.txt').exists())
    def test_office_extract_never_executes_links(self):
        p=self.root/'x.docx'
        with zipfile.ZipFile(p,'w') as z:
            z.writestr('word/document.xml','<document><t>Visible text</t></document>');z.writestr('word/_rels/document.xml.rels','<Relationship Target="https://example.invalid/"/>')
        dest=self.root/'out';b.process(self.item(p,'DOCX'),dest)
        r=json.loads((dest/'result.json').read_text());self.assertIn('Visible text',r['text']);self.assertTrue(r['partial'])
    def test_xml_entities_rejected(self):
        with self.assertRaises(ValueError):b.safe_xml(b'<!DOCTYPE x [<!ENTITY a SYSTEM "file:///secret">]><x>&a;</x>')
    def test_spreadsheet_shared_strings_and_cached_formula(self):
        p=self.root/'x.xlsx';ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'
        with zipfile.ZipFile(p,'w') as z:
            z.writestr('xl/workbook.xml',f'<workbook xmlns="{ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Datos" r:id="r1"/></sheets></workbook>')
            z.writestr('xl/_rels/workbook.xml.rels','<Relationships><Relationship Id="r1" Target="worksheets/sheet1.xml"/></Relationships>')
            z.writestr('xl/sharedStrings.xml',f'<sst xmlns="{ns}"><si><t>Nombre</t></si></sst>')
            z.writestr('xl/worksheets/sheet1.xml',f'<worksheet xmlns="{ns}"><sheetData><row r="4"><c r="A4" t="s"><v>0</v></c><c r="C4"><f>1+1</f><v>2</v></c></row></sheetData></worksheet>')
        with zipfile.ZipFile(p) as z:s=b.spreadsheet(z)
        self.assertEqual(s[0]['name'],'Datos');self.assertEqual(s[0]['rows'][0][0],'4');self.assertEqual(s[0]['rows'][0][1],'Nombre');self.assertEqual(s[0]['rows'][0][2],'');self.assertIn('2 [valor guardado',s[0]['rows'][0][3])

if __name__=='__main__':unittest.main()
