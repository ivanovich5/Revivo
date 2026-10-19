"""Inventory a staged distribution without executing binaries or reading USBs.

Usage: python audit_distribution.py STAGE OUTPUT.json
Import tables are evidence of direct dependencies, not a complete SBOM: dynamic
loading, statically linked code and delay imports require separate review.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys


def pe_imports(path):
    data = path.read_bytes()
    if data[:2] != b'MZ':
        return []
    pe = struct.unpack_from('<I', data, 60)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('Invalid PE signature')
    sections = struct.unpack_from('<H', data, pe + 6)[0]
    opt_size = struct.unpack_from('<H', data, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from('<H', data, opt)[0]
    if magic not in (0x10b, 0x20b):
        raise ValueError('Unsupported PE optional header')
    directories = opt + (112 if magic == 0x20b else 96)
    rva = struct.unpack_from('<I', data, directories + 8)[0]
    if not rva:
        return []
    table = opt + opt_size

    def offset(address):
        for i in range(sections):
            size, start, raw_size, raw = struct.unpack_from('<IIII', data, table + i * 40 + 8)
            if start <= address < start + max(size, raw_size):
                return raw + address - start
        raise ValueError('RVA outside sections')

    pos = offset(rva)
    names = []
    for _ in range(4096):
        fields = struct.unpack_from('<IIIII', data, pos)
        if not any(fields):
            return sorted(set(names))
        name = offset(fields[3])
        end = data.index(b'\0', name, min(name + 512, len(data)))
        names.append(data[name:end].decode('ascii'))
        pos += 20
    raise ValueError('Import table exceeds limit')


def audit(root):
    files = []
    for path in sorted(root.rglob('*')):
        if not path.is_file() or '__pycache__' in path.parts:
            continue
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        row = {'path': path.relative_to(root).as_posix(), 'bytes': path.stat().st_size,
               'sha256': digest}
        if path.suffix.lower() in ('.exe', '.dll', '.pyd'):
            try:
                row['direct_imports'] = pe_imports(path)
            except (ValueError, struct.error, IndexError) as exc:
                row['inspection_error'] = str(exc)
        files.append(row)
    return {'schema': 1, 'scope': 'File hashes and PE direct imports; not license clearance',
            'files': files}


if __name__ == '__main__':
    root = Path(sys.argv[1]).resolve(strict=True)
    output = Path(sys.argv[2]).resolve()
    if output.is_relative_to(root):
        raise SystemExit('Write the inventory outside the stage')
    result = audit(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"Inventoried {len(result['files'])} files")
