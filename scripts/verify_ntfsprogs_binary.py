import struct,json,hashlib,argparse
from pathlib import Path
parser=argparse.ArgumentParser(description='Compare libntfs COFF text with distributed PE executables, excluding linker relocations.')
parser.add_argument('archive',type=Path);parser.add_argument('engine_directory',type=Path);parser.add_argument('output',type=Path)
args=parser.parse_args()
a=args.archive.read_bytes();pos=8;longnames=b'';objects=[]
while pos+60<=len(a):
 h=a[pos:pos+60];size=int(h[48:58]);name=h[:16].decode().strip();d=a[pos+60:pos+60+size];pos+=60+size+(size%2)
 if name=='//':longnames=d;continue
 if name in ('/','/SYM64/'):continue
 if name.startswith('/') and name[1:].isdigit():name=longnames[int(name[1:]):].split(b'/\n')[0].decode()
 if len(d)<20:continue
 machine,nsec,_,_,_,opt,_=struct.unpack_from('<HHIIIHH',d)
 if machine!=0x8664:continue
 for i in range(nsec):
  s=20+opt+40*i;sn=d[s:s+8].rstrip(b'\0').decode(errors='replace')
  if not sn.startswith('.text'):continue
  length,ptr,relptr=struct.unpack_from('<III',d,s+16);nr=struct.unpack_from('<H',d,s+32)[0]
  if length<32:continue
  data=d[ptr:ptr+length];mask=bytearray(b'\1'*length)
  for j in range(nr):
   off,_,typ=struct.unpack_from('<IIH',d,relptr+j*10)
   if typ not in (1,2,3,4,5,6,7,8,9):raise ValueError(f"Unsupported relocation: {typ}")
   width=8 if typ==1 else 4
   mask[off:off+width]=b'\0'*width
  # Linkers may replace trailing alignment bytes; exclude up to 15 trailing padding bytes.
  end=len(data)
  while end>len(data)-15 and data[end-1] in (0x90,0):end-=1
  data=data[:end];mask=mask[:end]
  spans=[];start=None
  for k,v in enumerate(mask+b'\0'):
   if v and start is None:start=k
   if not v and start is not None:spans.append((start,k));start=None
  anchor=max(spans,key=lambda x:x[1]-x[0]);objects.append((name,sn,data,mask,anchor))
result={}
for exe in ['testdisk_win.exe','photorec_win.exe']:
 p=args.engine_directory/exe;binary=p.read_bytes();matches=[]
 for name,sn,data,mask,(start,end) in objects:
  anchor=data[start:end];offset=binary.find(anchor);found=None
  while offset>=0:
   candidate=offset-start
   if candidate>=0 and candidate+len(data)<=len(binary) and all(not m or data[k]==binary[candidate+k] for k,m in enumerate(mask)):found=candidate;break
   offset=binary.find(anchor,offset+1)
  matches.append({'object':name,'section':sn,'bytes':len(data),'compared_bytes':sum(mask),'match_offset':found})
 result[exe]={'sha256':hashlib.sha256(binary).hexdigest(),'sections':matches,'matched':sum(x['match_offset'] is not None for x in matches),'total':len(matches)}
args.output.write_text(json.dumps(result,indent=2),encoding="utf-8")
for k,v in result.items():print(k,v['matched'],v['total']);print([(x['object'],x['bytes'],x['match_offset']) for x in v['sections']])
