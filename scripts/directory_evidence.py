"""Read-only orphan directory evidence search. Never invents a directory tree.

FAT: contiguous LFN sequence + short-name checksum, and dot/dotdot pairs.
exFAT: complete active entry sets + checksum and stream/name entries.
NTFS: aligned FILE records + USA fixups + bounded resident FILE_NAME attributes.
These are candidates, not proof of file allocation or recoverability.
"""
import argparse
import collections
import json
from pathlib import Path
import re
import struct

def u16(b, p): return struct.unpack_from('<H', b, p)[0]
def u32(b, p): return struct.unpack_from('<I', b, p)[0]
def cluster(b): return (u16(b, 20) << 16) | u16(b, 26)

def safe_name(name):
    return bool(name) and len(name) <= 255 and not any(ord(c)<32 or c in '/\\:*?"<>|' for c in name)

def checksum83(name):
    value = 0
    for c in name: value = (((value & 1) << 7) + (value >> 1) + c) & 255
    return value

def fat_lfn(data, p):
    n = data[p] & 31
    if not 1 <= n <= 20 or data[p] != 64+n or p+(n+1)*32 > len(data): return None
    pieces = {}
    check = data[p+13]
    for i in range(n):
        e = data[p+i*32:p+(i+1)*32]
        if e[0] != (n-i | (64 if i==0 else 0)) or e[11:13] != b'\x0f\0' or e[13]!=check or e[26:28]!=b'\0\0': return None
        pieces[n-i] = e[1:11]+e[14:26]+e[28:32]
    e=data[p+n*32:p+(n+1)*32]
    if e[0] in (0,229) or e[11]&0xc8 or checksum83(e[:11])!=check: return None
    raw=b''.join(pieces[i] for i in range(1,n+1))
    try: name=raw.decode('utf-16-le').split('\0')[0].rstrip('\uffff')
    except UnicodeError: return None
    if not safe_name(name): return None
    c=cluster(e); size=u32(e,28)
    if c>=0x0ffffff0 or (size>0 and c<2): return None
    return {'name':name,'shortName':e[:11].decode('cp437'), 'cluster':c,'size':size,'directory':bool(e[11]&16),'lfnChecksumVerified':True}

def exfat(data,p):
    count=data[p+1]+1
    if not 3<=count<=19 or p+count*32>len(data): return None
    b=data[p:p+count*32]
    if b[0]!=0x85 or b[32]!=0xc0 or any(b[i]!=0xc1 for i in range(64,len(b),32)): return None
    checksum=0
    for i,c in enumerate(b):
        if i not in (2,3): checksum=(((checksum&1)<<15)+(checksum>>1)+c)&65535
    if checksum!=u16(b,2): return None
    length=b[35]
    if not 1<=length<=255 or (length+14)//15 != count-2: return None
    try: name=b''.join(b[i+2:i+32] for i in range(64,len(b),32))[:length*2].decode('utf-16-le')
    except UnicodeError: return None
    if not safe_name(name): return None
    return {'name':name,'cluster':u32(b,52),'size':struct.unpack_from('<Q',b,56)[0],'directory':bool(u16(b,4)&16),'entrySetChecksumVerified':True}

def ntfs(data,p):
    if p+48>len(data): return None
    allocated=u32(data,p+28); used=u32(data,p+24)
    if allocated not in (1024,4096) or not 48<=used<=allocated or p+allocated>len(data): return None
    b=bytearray(data[p:p+allocated]); usa=u16(b,4); count=u16(b,6)
    if count!=allocated//512+1 or usa<8 or usa+2*count>512: return None
    for i in range(1,count):
        end=i*512-2
        if b[end:end+2]!=b[usa:usa+2]: return None
        b[end:end+2]=b[usa+2*i:usa+2*i+2]
    pos=u16(b,20); names=[]; ended=False
    if pos<48 or pos%8: return None
    while pos+8<=used:
        typ=u32(b,pos)
        if typ==0xffffffff: ended=True; break
        length=u32(b,pos+4)
        if length<24 or length%8 or pos+length>used: return None
        if typ==0x30 and b[pos+8]==0:
            length_value=u32(b,pos+16); start=u16(b,pos+20)
            if start<24 or start+length_value>length or length_value<66: return None
            v=b[pos+start:pos+start+length_value]; n=v[64]
            if 66+2*n>len(v): return None
            try: name=v[66:66+2*n].decode('utf-16-le')
            except UnicodeError: return None
            if safe_name(name): names.append({'name':name,'parentReference':struct.unpack_from('<Q',v,0)[0],'namespace':v[65]})
        pos+=length
    if not ended or not names: return None
    return {'recordNumber':u32(b,44),'sequence':u16(b,16),'flags':u16(b,22),'names':names,'usaVerified':True}

def scan(image, output):
    image=Path(image).resolve()
    if image.suffix.lower()!='.img' or not image.is_file(): raise ValueError('Only local .img files are accepted')
    records=[]; seen=set(); size=image.stat().st_size; blocksize=16*1024*1024; overlap=8192
    patterns=[('FAT_LFN',re.compile(rb'[\x41-\x54].{10}\x0f\x00',re.S),fat_lfn),
              ('exFAT',re.compile(rb'\x85[\x02-\x12].{30}\xc0',re.S),exfat),
              ('NTFS',re.compile(b'FILE'),ntfs),
              ('FAT_DOTS',re.compile(rb'\. {10}\x10.{20}\.\. {9}\x10',re.S),None)]
    with image.open('rb') as f:
        offset=0; tail=b''
        while data:=f.read(blocksize):
            b=tail+data; base=offset-len(tail)
            for kind,pattern,parser in patterns:
                for m in pattern.finditer(b):
                    pos=m.start(); absolute=base+pos
                    if absolute % (512 if kind=='NTFS' else 32) or (kind,absolute) in seen: continue
                    if parser: result=parser(b,pos)
                    else:
                        if pos+64>len(b): continue
                        a=b[pos:pos+32]; z=b[pos+32:pos+64]
                        result={'cluster':cluster(a),'parentCluster':cluster(z)} if u32(a,28)==0 and u32(z,28)==0 and 2<=cluster(a)<0x0ffffff0 and cluster(z)<0x0ffffff0 else None
                    if result:
                        seen.add((kind,absolute)); records.append({'kind':kind,'offset':absolute,**result})
            offset+=len(data); tail=b[-overlap:]
            if offset%(1024*1024*1024)==0: print(f'{offset/size:.0%} | {len(records)} evidencias',flush=True)
    report={'image':str(image),'imageBytes':size,'counts':dict(collections.Counter(r['kind'] for r in records)),
            'records':records,'limitations':['Registros huérfanos: no establecen por sí solos una jerarquía ni la ubicación del contenido.',
            'No se buscan nombres largos FAT eliminados ni conjuntos exFAT inactivos. Las entradas cortas aisladas no se aceptan por su elevada tasa de falsos positivos.',
            'Ausencia de registros aceptados no demuestra ausencia total de metadatos recuperables.']}
    Path(output).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report['counts']))

if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('image'); p.add_argument('output'); args=p.parse_args(); scan(args.image,args.output)
