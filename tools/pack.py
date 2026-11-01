"""Create a portable offline package without state, credentials or test files."""
import argparse,hashlib,json,zipfile
from pathlib import Path
from check_dependencies import check

def sha256(path):
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()

def package(root,output,with_vulkan=False):
    root=Path(root).resolve();output=Path(output).resolve();output.parent.mkdir(parents=True,exist_ok=True)
    documents=['MiniAI.exe','README.md','README.zh-CN.md','LICENSE','API.md','Models.md','THIRD-PARTY.md','RELEASE.md','Verification.md','verification.json','assets.json']
    paths=[root/name for name in documents]
    for folder in ('models/ocr','models/ocr-fast','models/formula','runtime/translation'):
        paths.extend(p for p in (root/folder).rglob('*') if p.is_file() and (with_vulkan or folder!='runtime/translation' or 'vulkan' not in p.relative_to(root/folder).parts) and p.name!='cudnn64_9.dll' and p.suffix not in ('.part','.pyi','.h','.a','.lib') and not set(p.relative_to(root/folder).parts)&{'tests','__pycache__','include','test'})
    paths.extend(p for p in (root/'models/translation').glob('*') if p.is_file() and p.name in ('hy-mt2.gguf','model-provenance.json'))
    excluded_licenses=('OPUS','CTranslate','Intel','MKL','oneDNN','OpenMP','SentencePiece')
    paths.extend(p for p in (root/'licenses').iterdir() if p.is_file() and not any(word.lower() in p.name.lower() for word in excluded_licenses))
    paths.extend(p for p in (root/'runtime').iterdir() if p.is_file() and p.suffix.lower()=='.dll')
    for path in paths:
        if not path.is_file():raise FileNotFoundError(path)
    paths=sorted(set(paths));manifest={path.relative_to(root).as_posix():{'bytes':path.stat().st_size,'sha256':sha256(path)} for path in paths}
    lowered=[name.lower() for name in manifest]
    if any('opus' in name or 'ctranslate' in name or 'translation-ct2' in name for name in lowered):raise ValueError('Hy-only release cannot contain OPUS-MT or CTranslate2 assets.')
    if 'models/translation/hy-mt2.gguf' not in manifest:raise FileNotFoundError('Hy-MT2 model is required in the offline package.')
    for name in ('models/formula/encoder.onnx','models/formula/decoder.onnx','models/formula/image_resizer.onnx','models/formula/tokenizer.json'):
        if name not in manifest:raise FileNotFoundError('Formula OCR asset is required: '+name)
    if not any(name.startswith('runtime/translation/avx2/llama-server') for name in manifest):raise FileNotFoundError('Hy STQ AVX2 runtime is required.')
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
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--with-vulkan',action='store_true',help='Include the optional GPU runtime; CPU-only is the compact default.');args=parser.parse_args();package(args.root,args.output,args.with_vulkan)
