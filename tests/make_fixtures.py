"""Create deterministic FAT32 images in ordinary files. Never mounts a volume."""
import hashlib
import json
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1] / "demo"
ROOT.mkdir(exist_ok=True)
SECTOR = 512
SECTORS = 131072  # 64 MiB; enough clusters for a genuine FAT32 volume
FAT_SECTORS = 1024
DATA_SECTOR = 32 + 2 * FAT_SECTORS
image = bytearray(SECTORS * SECTOR)

def u16(offset, value): struct.pack_into("<H", image, offset, value)
def u32(offset, value): struct.pack_into("<I", image, offset, value)
image[0:3] = b"\xeb\x58\x90"
image[3:11] = b"MSWIN4.1"
u16(11, 512)
image[13] = 1
u16(14, 32)
image[16] = 2
image[21] = 0xF8
u16(24, 63)
u16(26, 255)
u32(32, SECTORS)
u32(36, FAT_SECTORS)
u32(44, 2)
u16(48, 1)
u16(50, 6)
image[64] = 0x80
image[66] = 0x29
u32(67, 0x52534354)
image[71:82] = b"RESCATE USB"
image[82:90] = b"FAT32   "
image[510:512] = b"\x55\xaa"
u32(512, 0x41615252)
u32(512 + 484, 0x61417272)
u32(512 + 488, 0xFFFFFFFF)
u32(512 + 492, 0xFFFFFFFF)
u32(512 + 508, 0xAA550000)
image[6*512:7*512] = image[:512]
image[7*512:8*512] = image[512:1024]

def fat(cluster, next_cluster):
    for copy in range(2):
        u32((32 + copy * FAT_SECTORS) * 512 + cluster * 4, next_cluster)

def location(cluster): return (DATA_SECTOR + cluster - 2) * 512
fat(0, 0x0FFFFFF8)
fat(1, 0xFFFFFFFF)
fat(2, 0x0FFFFFFF)

def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

pixels = b"".join(b"\x00" + bytes(c for x in range(64) for c in (x * 4, y * 4, 120)) for y in range(64))
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 64, 64, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(pixels)) + chunk(b"IEND", b"")
samples = [
    (b"HELLO   TXT", b"Prueba de Rescate USB. Archivo intacto.\r\n", False, False),
    (b"FRAG    TXT", (b"Fragmented known data 0123456789\r\n" * 80), False, True),
    (b"PHOTO   PNG", png, False, False),
    (b"LOST    TXT", b"Archivo eliminado de prueba; el contenido sigue disponible.\r\n", True, False),
]
next_cluster = 3
manifest = []
for index, (short, data, deleted, fragmented) in enumerate(samples):
    count = (len(data) + 511) // 512
    clusters = [next_cluster + i * (2 if fragmented else 1) for i in range(count)]
    next_cluster = clusters[-1] + 2
    for i, cluster in enumerate(clusters):
        image[location(cluster):location(cluster) + min(512, len(data)-i*512)] = data[i*512:(i+1)*512]
        fat(cluster, 0 if deleted else clusters[i+1] if i + 1 < count else 0x0FFFFFFF)
    offset = location(2) + index * 32
    image[offset:offset+11] = short
    if deleted: image[offset] = 0xE5
    image[offset+11] = 0x20
    u16(offset+20, clusters[0] >> 16)
    u16(offset+26, clusters[0] & 0xFFFF)
    u32(offset+28, len(data))
    name = short[:8].decode().strip() + "." + short[8:].decode().strip()
    manifest.append({"Name": name, "Size": len(data), "Sha256": hashlib.sha256(data).hexdigest().upper(), "Deleted": deleted, "Fragmented": fragmented})

(ROOT / "fat32-demo.img").write_bytes(image)
# Same image behind a valid MBR partition, sector offset 2048.
disk = bytearray(2048 * 512) + image
disk[446 + 4] = 0x0C
struct.pack_into("<II", disk, 446 + 8, 2048, SECTORS)
disk[510:512] = b"\x55\xaa"
(ROOT / "fat32-mbr.img").write_bytes(disk)
# Destroy metadata, retaining content for signature recovery. Do this only in a new file.
image[:DATA_SECTOR*512] = bytes(DATA_SECTOR*512)
image[location(2):location(2)+512] = bytes(512)
(ROOT / "raw-damaged.img").write_bytes(image)
(ROOT / "expected.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(f"Created three synthetic images and {len(manifest)} reference hashes in {ROOT}")
