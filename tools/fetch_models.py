"""Fetch pinned offline assets using only the Python standard library.

Downloads and temporary files stay below the selected MiniAI folder.
"""
import argparse,concurrent.futures,hashlib,json,os,time,urllib.request,zipfile
from pathlib import Path

def digest(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):h.update(block)
    return h.hexdigest()

class NoRanges(Exception):pass

def parallel_download(url,path,size,opener,timeout):
    chunk=8*1024*1024
    def read(start):
        end=min(size,start+chunk)-1
        request=urllib.request.Request(url,headers={'User-Agent':'MiniAI-asset-installer/0.2','Range':'bytes=%d-%d'%(start,end)})
        with opener.open(request,timeout=timeout) as response:
            if response.status!=206:raise NoRanges()
            value=response.read(end-start+1)
            if len(value)!=end-start+1:raise IOError('Incomplete asset range')
            return start,value
    with path.open('w+b') as output,concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        futures=[pool.submit(read,start) for start in range(0,size,chunk)]
        for future in concurrent.futures.as_completed(futures):
            offset,value=future.result();output.seek(offset);output.write(value)

def fetch(urls,path,sha,size):
    errors=[]
    for attempt in range(3):
        try:return fetch_once(urls,path,sha,size)
        except RuntimeError as error:
            errors.append(str(error))
            if attempt<2:time.sleep(2*(attempt+1))
    raise RuntimeError('Asset fetch failed after retries: '+'; '.join(errors))

def fetch_once(urls,path,sha,size):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists() and digest(path)==sha:return
    errors=[];pending=path.with_suffix(path.suffix+'.part')
    for url in urls:
        for direct in (True,False):
            try:
                opener=urllib.request.build_opener(urllib.request.ProxyHandler({})) if direct else urllib.request.build_opener()
                if size>=32*1024*1024:
                    try:
                        parallel_download(url,pending,size,opener,15 if direct else 45)
                        if pending.stat().st_size!=size or digest(pending)!=sha:raise ValueError('Size or SHA-256 mismatch')
                        os.replace(pending,path);print('Verified:',path.name,size,flush=True);return
                    except (NoRanges,OSError,ValueError):pass
                request=urllib.request.Request(url,headers={'User-Agent':'MiniAI-asset-installer/0.1'})
                with opener.open(request,timeout=8 if direct else 45) as response,pending.open('wb') as output:
                    try:response.fp.raw._sock.settimeout(120)
                    except AttributeError:pass
                    total=0
                    while True:
                        data=response.read(1024*1024)
                        if not data:break
                        total+=len(data)
                        if total>size:raise ValueError('Unexpected download size')
                        output.write(data)
                if total!=size or digest(pending)!=sha:raise ValueError('Size or SHA-256 mismatch')
                os.replace(pending,path);print('Verified:',path.name,size,flush=True);return
            except Exception as error:errors.append(type(error).__name__+': '+str(error))
    raise RuntimeError('Cannot fetch '+path.name+'; '+'; '.join(errors))

def ocr_assets(root,manifest):
    items=manifest['ocr']+manifest.get('ocr_fast',[])
    missing=[item for item in items if not (root/item['path']).exists() or digest(root/item['path'])!=item['sha256']]
    mirror=manifest.get('ocr_archive')
    if not missing or not mirror:return
    archive=root/'work/downloads/ocr-models.zip'
    fetch([mirror['url']],archive,mirror['sha256'],mirror['bytes'])
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:raise ValueError('OCR dependency archive is damaged')
        for item in missing:
            target=(root/item['path']).resolve()
            if not target.is_relative_to(root):raise ValueError('Invalid OCR destination')
            entry=bundle.getinfo(item['path'])
            if entry.file_size!=item['bytes']:raise ValueError('Unexpected OCR file size')
            data=bundle.read(entry)
            if hashlib.sha256(data).hexdigest()!=item['sha256']:raise ValueError('OCR asset checksum mismatch')
            target.parent.mkdir(parents=True,exist_ok=True);pending=target.with_suffix(target.suffix+'.part');pending.write_bytes(data);os.replace(pending,target)
    print('Verified pinned OCR dependency archive.',flush=True)

def install(root):
    root=Path(root).resolve();manifest=json.loads((Path(__file__).resolve().parents[1]/'assets.json').read_text(encoding='utf-8'))
    work=root/'work/downloads';work.mkdir(parents=True,exist_ok=True)
    try:ocr_assets(root,manifest)
    except (OSError,ValueError,RuntimeError) as error:print('OCR mirror unavailable; trying upstream: '+str(error),flush=True)
    formula=manifest.get('formula',{}).get('files',[])
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        tasks=[pool.submit(fetch,item['urls'],root/item['path'],item['sha256'],item['bytes']) for item in manifest['ocr']+manifest.get('ocr_fast',[])+formula]
        for task in tasks:task.result()
    # The historical GGUF type identifiers are adapted without requantizing
    # any tensor. Only the exact pinned upstream model is accepted.
    translation=manifest['translation'];model=translation['model'];target=root/model['path']
    if not target.exists() or digest(target)!=model['sha256']:
        url='https://huggingface.co/'+translation['upstream_repo']+'/resolve/'+translation['upstream_revision']+'/'+translation['upstream_file']
        fetch([url],target,translation['upstream_sha256'],model['bytes'])
        from prepare_model import prepare
        prepare(target)
        if digest(target)!=model['sha256']:raise ValueError('Prepared Hy-MT2 checksum mismatch')
    required=translation['runtime']
    missing=[item for item in required if not (root/item['path']).exists() or digest(root/item['path'])!=item['sha256']]
    if missing:
        native=translation['runtime_archive'];archive=work/'native-windows-x64.zip'
        fetch([native['url']],archive,native['sha256'],native['bytes'])
        with zipfile.ZipFile(archive) as bundle:
            if bundle.testzip() is not None:raise ValueError('Native runtime ZIP is damaged')
            for item in missing:
                destination=(root/item['path']).resolve()
                if not destination.is_relative_to(root):raise ValueError('Invalid native destination')
                entry=bundle.getinfo(item['path'])
                if entry.file_size!=item['bytes']:raise ValueError('Unexpected native file size')
                data=bundle.read(entry)
                if hashlib.sha256(data).hexdigest()!=item['sha256']:raise ValueError('Native checksum mismatch')
                destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
    ort=manifest['onnxruntime'];missing=[item for item in ort['files'] if not (root/item['path']).exists() or digest(root/item['path'])!=item['sha256']]
    if missing:
        archive=work/'onnxruntime-win-x64-1.20.1.zip'
        # Verify exact extracted payloads rather than trusting archive metadata.
        for direct in (True,False):
            try:
                opener=urllib.request.build_opener(urllib.request.ProxyHandler({})) if direct else urllib.request.build_opener()
                with opener.open(ort['url'],timeout=8 if direct else 60) as response,archive.open('wb') as output:
                    total=0
                    while True:
                        data=response.read(1024*1024)
                        if not data:break
                        total+=len(data)
                        if total>100*1024*1024:raise ValueError('Unexpected runtime archive size')
                        output.write(data)
                with zipfile.ZipFile(archive) as bundle:
                    for item in missing:
                        entry=next(x for x in bundle.infolist() if x.filename.endswith('/lib/'+Path(item['path']).name))
                        if entry.file_size!=item['bytes']:raise ValueError('Unexpected runtime DLL size')
                        data=bundle.read(entry)
                        if hashlib.sha256(data).hexdigest()!=item['sha256']:raise ValueError('Runtime DLL checksum mismatch')
                        target=root/item['path'];target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
                break
            except Exception:
                if not direct:raise
    print('Hy-MT2/STQ, text OCR, formula OCR and ONNX Runtime verified. Add app-local VC++ DLLs from the release build.',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);install(parser.parse_args().root)
