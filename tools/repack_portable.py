"""Update an existing compact portable payload without re-encoding unchanged models.

The output is an internal EXE payload, not the ordinary distribution ZIP.
Unchanged ZIP records are copied verbatim; their logical SHA-256 and lossless
codec entries remain intact. Changed files get new logical hashes.
"""
import argparse,copy,hashlib,json,pathlib,shutil,struct,zipfile

def repack(original,source,runtime,output):
    original,source,runtime,output=map(pathlib.Path,(original,source,runtime,output))
    output.parent.mkdir(parents=True,exist_ok=True)
    documents=['MiniAI.exe','README.md','README.zh-CN.md','LICENSE','API.md','Models.md','THIRD-PARTY.md','RELEASE.md','Verification.md','verification.json','assets.json']
    changes={name:source/name for name in documents}
    for name in ['licenses/Texo-AGPL-3.0.txt','licenses/CrispEmbed-LICENSE.txt','licenses/ggml-MIT.txt']:changes[name]=source/name
    changes['models/formula/texo-distill-q8_0.gguf']=runtime/'models/formula/texo-distill-q8_0.gguf'
    assets=json.loads((source/'assets.json').read_text(encoding='utf-8-sig'))
    for item in assets['formula']['runtime']['files']:changes[item['path']]=runtime/item['path']
    for name,path in changes.items():
        if not path.is_file():raise FileNotFoundError(path)
        if '..' in pathlib.PurePosixPath(name).parts or name.startswith('/'):raise ValueError('Invalid payload path')
    with zipfile.ZipFile(original) as old,original.open('rb') as raw,zipfile.ZipFile(output,'w',allowZip64=False) as new:
        manifest=json.loads(old.read('MiniAI/package-checksums.json'))
        codecs=json.loads(old.read('MiniAI/portable-codecs.json'))['files'];reused=0
        for entry in old.infolist():
            name=entry.filename.removeprefix('MiniAI/')
            if name in ['package-checksums.json','portable-codecs.json']:continue
            if name.startswith('models/formula/') or name.startswith('runtime/formula/') or name in changes:
                manifest.pop(name,None);codecs.pop(name,None);continue
            if entry.flag_bits&8 or entry.file_size>=0xffffffff:raise ValueError('Unsupported ZIP data descriptor/ZIP64')
            if name not in manifest:raise ValueError('Unmanifested source entry')
            raw.seek(entry.header_offset);header=raw.read(30)
            if header[:4]!=b'PK\x03\x04':raise ValueError('Invalid ZIP record')
            size=30+sum(struct.unpack_from('<HH',header,26))+entry.compress_size
            info=copy.copy(entry);new._writecheck(info);info.header_offset=new.fp.tell()
            raw.seek(entry.header_offset);remaining=size
            while remaining:
                data=raw.read(min(1024*1024,remaining))
                if not data:raise EOFError('Truncated existing payload')
                new.fp.write(data);remaining-=len(data)
            new.start_dir=new.fp.tell();new.filelist.append(info);new.NameToInfo[info.filename]=info;new._didModify=True;reused+=1
        for name,path in changes.items():
            value=hashlib.sha256()
            with path.open('rb') as stream:
                for block in iter(lambda:stream.read(1024*1024),b''):value.update(block)
            manifest[name]={'bytes':path.stat().st_size,'sha256':value.hexdigest()}
            new.write(path,'MiniAI/'+name,compress_type=zipfile.ZIP_DEFLATED,compresslevel=6)
        new.writestr('MiniAI/package-checksums.json',json.dumps(manifest,indent=2)+'\n',compress_type=zipfile.ZIP_DEFLATED)
        new.writestr('MiniAI/portable-codecs.json',json.dumps({'format':1,'files':codecs},separators=(',',':')),compress_type=zipfile.ZIP_DEFLATED)
    with zipfile.ZipFile(output) as verify:
        if verify.testzip() is not None:raise ValueError('Updated payload CRC failed')
        if len(verify.infolist())!=len(manifest)+2:raise ValueError('Unexpected payload entries')
    print(json.dumps({'files':len(manifest),'reused_entries':reused,'bytes':output.stat().st_size}),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--original',required=True,type=pathlib.Path);p.add_argument('--source',required=True,type=pathlib.Path);p.add_argument('--runtime',required=True,type=pathlib.Path);p.add_argument('--output',required=True,type=pathlib.Path);a=p.parse_args();repack(a.original,a.source,a.runtime,a.output)
