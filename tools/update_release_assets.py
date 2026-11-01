"""Verify a release lockfile without discarding pinned download provenance."""
import hashlib,json
from pathlib import Path

def verify(root):
    root=Path(root);manifest=json.loads((root/'assets.json').read_text(encoding='utf-8'))
    if manifest['format']!=4:raise ValueError('Expected asset manifest format 4')
    items=manifest['ocr']+manifest['ocr_fast']+manifest['onnxruntime']['files']+manifest['formula']['files']+[manifest['translation']['model']]+manifest['translation']['runtime']
    for item in items:
        path=root/item['path'];digest=hashlib.sha256()
        with path.open('rb') as stream:
            while data:=stream.read(1024*1024):digest.update(data)
        if path.stat().st_size!=item['bytes'] or digest.hexdigest()!=item['sha256']:raise ValueError('Pinned asset changed: '+item['path'])
    print(json.dumps({'verified_assets':len(items),'format':manifest['format']}))
if __name__=='__main__':verify(Path(__file__).resolve().parents[1])
