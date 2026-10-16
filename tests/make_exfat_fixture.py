"""Test fixture only; FATtools is never used by the desktop recovery application.
Usage: python tests/make_exfat_fixture.py <path-to-FATtools-source>
Pinned repository: maxpat78/FATtools @ aa29362350ce1cf6488f18ac302951ddb2aa1b88
"""
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(sys.argv[1]).resolve()))
from FATtools.Volume import vopen, vclose
from FATtools.mkfat import exfat_mkfs

demo = Path(__file__).resolve().parents[1] / "demo"
path = demo / "exfat-demo.img"
with path.open("wb") as file:
    file.truncate(64 * 1024 * 1024)
disk = vopen(str(path), "r+b", "disk")
exfat_mkfs(disk, disk.size)
vclose(disk)
root = vopen(str(path), "r+b")
expected = []
for name, content in [("Documento de prueba.txt", b"exFAT known test data\r\n"), ("datos.bin", bytes(range(251)) * 200)]:
    file = root.create(name)
    file.write(content)
    file.close()
    expected.append({"Name": name, "Sha256": hashlib.sha256(content).hexdigest().upper()})
vclose(root)
(demo / "exfat-expected.json").write_text(json.dumps(expected, indent=2), encoding="utf-8")
print("Created synthetic exFAT image:", path)
