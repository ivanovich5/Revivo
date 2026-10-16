import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from local_paths import local_path, display_path
from rigorous_review import atomic_json
from preview_export import prepare

class LongPaths(unittest.TestCase):
    def test_extended_validation_and_previews_preserve_originals(self):
        with tempfile.TemporaryDirectory() as directory:
            root=local_path(directory)/('ruta-'+ 'a'*65)/('caso-'+ 'b'*65)/('recuperados-'+ 'c'*65)
            root.mkdir(parents=True)
            import fitz
            pdf=root/'ejemplo.pdf'
            with fitz.open() as doc:
                doc.new_page().insert_text((30,30),'Revivo long path fixture')
                doc.save(pdf)
            png=root/'imagen.png'
            from PIL import Image
            Image.new('RGB',(20,20),(20,40,60)).save(png)
            rows=[]
            for p in [pdf,png]:
                rows.append({'OutputName':p.name,'Sha256':hashlib.sha256(p.read_bytes()).hexdigest(),
                    'OriginalName':p.name,'Method':'Metadatos','State':'Recuperado','Error':None})
            atomic_json(root/'informe.json',{'Files':rows})
            # A partial previous attempt must not prevent a new preview destination.
            (root/'vistas-integradas').mkdir()
            prepare(display_path(root),display_path(root/'vistas-nuevas'))
            catalog=json.loads((root/'vistas-nuevas/catalog.json').read_text(encoding='utf-8'))
            self.assertEqual(len(catalog),2)
            self.assertTrue(all(r['images'] for r in catalog))
            self.assertTrue(all(local_path(r['images'][0]).is_file() for r in catalog))
            self.assertGreater(len(str(root/'informes/validaciones-vistas-nuevas'/('a'*64+'.json.tmp'))),260)
            for row in rows:self.assertEqual(hashlib.sha256((root/row['OutputName']).read_bytes()).hexdigest(),row['Sha256'])
            with self.assertRaises(ValueError):prepare(root,root/'vistas-nuevas')

    def test_missing_parent_remains_an_error(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):atomic_json(local_path(directory)/'missing/x.json',{})

if __name__=='__main__':unittest.main()
