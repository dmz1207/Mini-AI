"""Create a portable offline package without state, credentials or test files."""
import argparse,hashlib,json,zipfile
from pathlib import Path
from check_dependencies import check

def sha256(path):
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()

def package(root,output):
    root=Path(root).resolve();output=Path(output).resolve();output.parent.mkdir(parents=True,exist_ok=True)
    documents=['MiniAI.exe','README.md','README.zh-CN.md','LICENSE','API.md','Models.md','THIRD-PARTY.md','Verification.md','verification.json','assets.json']
    paths=[root/name for name in documents]
    for folder in ('models/ocr','models/ocr-fast','models/translation/eng-zho','models/translation/zho-eng','runtime/translation-ct2','licenses'):
        paths.extend(p for p in (root/folder).rglob('*') if p.is_file() and p.name!='cudnn64_9.dll' and p.suffix not in ('.part','.pyi','.h','.a','.lib') and not set(p.relative_to(root/folder).parts)&{'tests','__pycache__','include','test'})
    paths.extend(p for p in (root/'runtime').iterdir() if p.is_file() and p.suffix.lower()=='.dll')
    for path in paths:
        if not path.is_file():raise FileNotFoundError(path)
    paths=sorted(set(paths));manifest={path.relative_to(root).as_posix():{'bytes':path.stat().st_size,'sha256':sha256(path)} for path in paths}
    check([path for path in paths if path.suffix.lower() in ('.dll','.exe','.pyd')])
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for path in paths:archive.write(path,'MiniAI/'+path.relative_to(root).as_posix())
        archive.writestr('MiniAI/package-checksums.json',json.dumps(manifest,indent=2)+'\n')
    with zipfile.ZipFile(output) as archive:
        assert not any('/state/' in name or 'connection.json' in name or name.endswith('.part') for name in archive.namelist())
        for name,info in manifest.items():
            result=hashlib.sha256()
            with archive.open('MiniAI/'+name) as stream:
                for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
            if result.hexdigest()!=info['sha256']:raise ValueError('Archive round trip failed: '+name)
    record={'files':len(paths),'uncompressed_bytes':sum(info['bytes'] for info in manifest.values()),'zip_bytes':output.stat().st_size,'zip_sha256':sha256(output)}
    output.with_suffix('.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8');print(json.dumps(record),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();package(args.root,args.output)
