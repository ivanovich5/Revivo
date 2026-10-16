"""Read-only, case-specific FAT32 reconstruction with explicit evidence.
Uses the surviving BPB as a geometry hypothesis, validated by independent dot
entries and parent links. FAT chains require both copies to agree at each step.
No repair, mount, sector writes, deleted entries or guessed chains.
"""
import collections
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import time
import directory_evidence as de

def clean(name):
    name=re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_',name).rstrip(' .')[:100]
    if not name or re.fullmatch(r'(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?',name):name='_'+name
    return name

def entries(data):
    pos=0
    while pos+32<=len(data):
        e=data[pos:pos+32]
        if e[0]==0:break
        if e[0]==229:pos+=32;continue
        if e[11]==15:
            record=de.fat_lfn(data,pos)
            if record:
                n=e[0]&31
                yield {**record,'entryByte':pos,'nameEvidence':'LFN y checksum 8.3'}
                pos+=(n+1)*32;continue
            pos+=32;continue
        if e[11]&0xc8 or e[:11] in (b'.          ',b'..         '):pos+=32;continue
        # Conservative short names. Long names with failed checksums are not guessed.
        short=e[:11]
        if any(c<32 or c in b'"*+,/:;<=>?[\\]|' for c in short) or short[0]==32:pos+=32;continue
        stem=short[:8].decode('cp437').rstrip();extension=short[8:].decode('cp437').rstrip()
        name=stem+('.'+extension if extension else '')
        c=de.cluster(e);length=de.u32(e,28)
        date=de.u16(e,24)
        if not de.safe_name(name) or c>=0xffffff0 or (length>0 and c<2) or not (1<=(date>>5)&15<=12 and 1<=date&31<=31):pos+=32;continue
        yield {'name':name,'shortName':short.decode('cp437'),'cluster':c,'size':length,'directory':bool(e[11]&16),'entryByte':pos,'nameEvidence':'Entrada corta 8.3 y fecha plausible'}
        pos+=32

def run(job):
    job=Path(job).resolve();image=job/'usb.img'; evidence=json.loads((job/'evidencias-directorios.json').read_text(encoding='utf-8'))
    output=job/('reconstruccion-carpetas-'+time.strftime('%Y%m%d-%H%M%S'));output.mkdir(exist_ok=False)
    report={'output':str(output),'directories':[],'files':[],'warnings':[]}
    with image.open('rb') as source:
        source.seek(13570768896);boot=source.read(512)
        bps=de.u16(boot,11);spc=boot[13];reserved=de.u16(boot,14);fatlen=de.u32(boot,36);fatcount=boot[16];root=de.u32(boot,44)
        if boot[510:512]!=b'\x55\xaa' or bps!=512 or spc!=16 or fatcount!=2:raise ValueError('Unexpected surviving geometry')
        cluster_size=bps*spc;data_start=(reserved+fatcount*fatlen)*bps;limit=(image.stat().st_size-data_start)//cluster_size+2
        anchors=[r for r in evidence['records'] if r['kind']=='FAT_DOTS' and r['offset']-(r['cluster']-2)*cluster_size==data_start]
        if len(anchors)<20:raise ValueError('Insufficient independent geometry anchors')
        source.seek(reserved*bps);fat1=source.read(fatlen*bps);fat2=source.read(fatlen*bps)
        report['geometry']={'sectorBytes':bps,'clusterBytes':cluster_size,'dataStart':data_start,'fat1Offset':reserved*bps,'fat2Offset':(reserved+fatlen)*bps,'independentDotAnchors':len(anchors),'rootCluster':root,'inferredVolumeStart':0,'note':'Surviving BPB is displaced. Data origin is a hypothesis supported by directory self-cluster positions; no boot repair was made.'}
        def next_cluster(c):
            if not 2<=c<limit or c*4+4>len(fat1):return None,'Cluster fuera de imagen'
            a=de.u32(fat1,c*4)&0xfffffff;b=de.u32(fat2,c*4)&0xfffffff
            if a!=b:return None,'Las dos FAT discrepan'
            if a>=0xffffff8:return None,'EOC'
            if 2<=a<limit:return a,None
            return None,'Valor FAT no válido'
        def chain(start,max_clusters):
            result=[];seen=set();c=start
            while c is not None and len(result)<max_clusters:
                if c in seen:return result,'Ciclo FAT'
                if not 2<=c<limit:return result,'Cluster fuera de imagen'
                seen.add(c);result.append(c);c,reason=next_cluster(c)
                if reason:return result,reason
            return result,'Límite de cadena'
        queue=[(root,0,[],[],True)];visited=set();orphan_anchors=list(anchors)
        while queue or orphan_anchors:
            if not queue:
                anchor=orphan_anchors.pop(0)
                if anchor['cluster'] in visited:continue
                queue.append((anchor['cluster'],anchor['parentCluster'],['Ramas_sin_raiz_confirmada',f"Rama_cluster_{anchor['cluster']}"],[],False))
            c,parent,names,ancestors,connected=queue.pop(0)
            if c in visited:continue
            if len(names)>24 or not 2<=c<limit:continue
            source.seek(data_start+(c-2)*cluster_size);first=source.read(cluster_size)
            if c!=root:
                if first[:11]!=b'.          ' or first[32:43]!=b'..         ' or de.cluster(first[:32])!=c or de.cluster(first[32:64]) not in ({0,root} if parent==root else {parent}):
                    report['warnings'].append({'path':'/'.join(names),'reason':'Enlace padre/hijo no confirmado; no se recorrió'});continue
            visited.add(c)
            clusters,reason=chain(c,128)
            # Include a known first cluster even when its FAT tail is damaged.
            data=bytearray()
            for cc in clusters:
                source.seek(data_start+(cc-2)*cluster_size);data+=source.read(cluster_size)
            dirpath='/'.join(names) or '/'
            report['directories'].append({'path':dirpath,'cluster':c,'parentCluster':parent,'clustersRead':len(clusters),'chainStatus':reason,'parentLinkVerified':c!=root,'rootConnected':connected})
            for e in entries(data):
                pathnames=names+[e['name']];entry={**e,'originalPath':'/'.join(pathnames),'parentCluster':c,'directoryChainStatus':reason,'rootConnected':connected}
                if e['directory']:
                    if e['cluster'] not in ancestors+[c]:queue.append((e['cluster'],c,pathnames,ancestors+[c],connected))
                    continue
                if e['size']==0:
                    entry.update(recovery='No extraído: entrada vacía');report['files'].append(entry);continue
                if Path(e['name']).suffix.lower() in {'.lnk','.exe','.dll','.bat','.cmd','.js','.vbs','.ps1','.scr','.com','.msi'}:
                    entry.update(recovery='No extraído: extensión activa');report['files'].append(entry);continue
                required=(e['size']+cluster_size-1)//cluster_size
                if required>131072:entry.update(recovery='No extraído: límite 1 GiB');report['files'].append(entry);continue
                chain_list,end=chain(e['cluster'],required+1)
                entry.update(chainStatus=end,requiredClusters=required,confirmedClusters=len(chain_list),startByte=data_start+(e['cluster']-2)*cluster_size)
                if len(chain_list)!=required or end!='EOC':
                    entry.update(recovery='No extraído: cadena no confirmada');report['files'].append(entry);continue
                # Place opaque files in staging only. Later validation assigns names/folders.
                ext=Path(e['name']).suffix.lower()
                if not re.fullmatch(r'\.[a-z0-9_]{1,12}',ext):ext='.bin'
                destination=output/'extraidos'/f"meta_{len(report['files']):06d}{ext}"
                destination.parent.mkdir(exist_ok=True);h=hashlib.sha256();remaining=e['size']
                with destination.open('xb') as target:
                    for cc in chain_list:
                        source.seek(data_start+(cc-2)*cluster_size);b=source.read(min(remaining,cluster_size))
                        if not b:raise IOError('Unexpected short image read')
                        target.write(b);h.update(b);remaining-=len(b)
                entry.update(recovery='Extraído: cadena completa concordante en ambas FAT',path=str(destination),sha256=h.hexdigest())
                report['files'].append(entry)
            if len(visited)%25==0:print(f'Carpetas {len(report["directories"])}; entradas {len(report["files"])}',flush=True)
    report['summary']={'directories':len(report['directories']),'fileEntries':len(report['files']),'extracted':sum('path' in x for x in report['files']),'reasons':dict(collections.Counter(r['recovery'] for r in report['files']))}
    (output/'reconstruccion.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    (job/'ultima-reconstruccion.json').write_text(json.dumps({'manifest':str(output/'reconstruccion.json'),**report['summary']},ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report['summary'],ensure_ascii=False,indent=2))

if __name__=='__main__':run(sys.argv[1])
