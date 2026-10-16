"""Regression checks for conservative identification and orphan metadata parsing."""
import sys
from pathlib import Path
import struct
import tempfile
import unittest
import zipfile
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
import directory_evidence as de
import rigorous_review as rr
from recover_fat_evidence import clean, entries
from thematic_sort import classify

class Checks(unittest.TestCase):
    def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
    def tearDown(self):self.tmp.cleanup()
    def write(self,name,data):p=self.root/name;p.write_bytes(data);return p
    def test_text_is_not_certified(self):self.assertEqual(rr.inspect_file(self.write('a.txt',b'Plain text, intact or partial.'))['status'],'04_Sin_validar')
    def test_pdf_fragment_is_not_document(self):self.assertEqual(rr.inspect_file(self.write('a.txt',b'21 0 obj\n<< /Type /Font >>\nendobj'))['type'],'Restos_PDF')
    def test_missing_pdf_header(self):self.assertEqual(rr.inspect_file(self.write('a.pdf',b'random fragment'))['type'],'Tipo_declarado_no_coincide')
    def test_active_link_excluded(self):self.assertEqual(rr.inspect_file(self.write('a.lnk',b'L\0\0\0\1\x14\2\0'))['status'],'06_No_copiar_contenido_activo')
    def test_executable_with_false_extension(self):self.assertEqual(rr.inspect_file(self.write('a.txt',b'MZ'+b'\0'*50))['status'],'06_No_copiar_contenido_activo')
    def test_zip_traversal_not_extracted(self):
        p=self.root/'bad.zip'
        with zipfile.ZipFile(p,'w') as z:z.writestr('../escape','data')
        self.assertEqual(rr.inspect_file(p)['status'],'02_Con_avisos_o_danos');self.assertFalse((self.root.parent/'escape').exists())
    def test_fake_office_root_rejected(self):
        p=self.root/'a.docx'
        with zipfile.ZipFile(p,'w') as z:z.writestr('word/document.xml','<notdocument/>')
        with self.assertRaises(ValueError):rr.inspect_file(p)
    def test_xml_entities_rejected(self):
        with self.assertRaises(ValueError):rr.xml(b'<!DOCTYPE x [<!ENTITY y "hello">]><x>&y;</x>')
    def test_real_pdf_all_pages(self):
        import fitz
        p=self.root/'ok.pdf';doc=fitz.open()
        for i in range(3):doc.new_page().insert_text((72,72),f'Page {i}')
        doc.save(p);doc.close();r=rr.inspect_file(p)
        self.assertEqual(r['status'],'01_Comprobacion_superada');self.assertEqual(r['pagesChecked'],3)
    def test_image_decode(self):
        from PIL import Image
        p=self.root/'a.png';Image.new('RGB',(12,12),'red').save(p)
        self.assertEqual(rr.inspect_file(p)['status'],'01_Comprobacion_superada')
        damaged=self.write('truncated.png',p.read_bytes()[:30])
        with self.assertRaises(Exception):rr.inspect_file(damaged)
    def test_fat_checksum(self):
        s=bytearray(32);s[:11]=b'EXAMPLE TXT';s[11]=32;struct.pack_into('<H',s,26,2);struct.pack_into('<I',s,28,12)
        l=bytearray(32);l[0]=65;l[11]=15;l[13]=de.checksum83(s[:11]);raw=('Example.txt\0').encode('utf-16-le').ljust(26,b'\xff');l[1:11]=raw[:10];l[14:26]=raw[10:22];l[28:32]=raw[22:]
        self.assertEqual(de.fat_lfn(bytes(l+s),0)['name'],'Example.txt')
        l[13]^=1;self.assertIsNone(de.fat_lfn(bytes(l+s),0))
    def test_exfat_checksum(self):
        b=bytearray(96);b[0]=0x85;b[1]=2;b[4]=32;b[32]=0xc0;b[35]=8;b[52]=2;b[56]=9;b[64]=0xc1;b[66:82]='test.txt'.encode('utf-16-le')
        c=0
        for i,x in enumerate(b):
            if i not in (2,3):c=(((c&1)<<15)+(c>>1)+x)&65535
        struct.pack_into('<H',b,2,c);self.assertEqual(de.exfat(b,0)['name'],'test.txt')
        b[70]^=1;self.assertIsNone(de.exfat(b,0))
    def test_ntfs_usa_and_filename(self):
        b=bytearray(1024);b[:4]=b'FILE';struct.pack_into('<HH',b,4,48,3);struct.pack_into('<H',b,20,56);struct.pack_into('<II',b,24,176,1024)
        b[48:50]=b'XY';b[510:512]=b'XY';b[1022:1024]=b'XY'
        struct.pack_into('<II',b,56,0x30,112);struct.pack_into('<IH',b,72,82,24);b[144]=8;b[145]=1;b[146:162]='test.txt'.encode('utf-16-le');b[168:172]=b'\xff'*4
        self.assertEqual(de.ntfs(b,0)['names'][0]['name'],'test.txt')
        b[510]^=1;self.assertIsNone(de.ntfs(b,0))
    def test_unsafe_name_not_used_as_path(self):
        self.assertFalse(de.safe_name('../file'));self.assertEqual(clean('CON'),'_CON');self.assertNotIn('/',clean('one/two'))
    def test_ambiguous_theme_left_general(self):self.assertEqual(classify('cardiac cancer diabetes')['subtopic'],'General_o_tema_mixto')
    def test_theme_requires_multiple_terms(self):self.assertEqual(classify('cancer '*100)['subtopic'],'General_o_tema_mixto')

if __name__=='__main__':unittest.main(verbosity=2)
