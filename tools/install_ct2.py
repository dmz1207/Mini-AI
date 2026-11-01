"""Install the hash-pinned, private inference runtime and INT8 models."""
import hashlib,json,shutil,subprocess,zipfile
from pathlib import Path

def install(root,manifest,fetch,digest):
    root=Path(root).resolve();runtime=root/'runtime/translation-ct2';downloads=root/'work/downloads';downloads.mkdir(parents=True,exist_ok=True)
    for item in manifest['translation_runtime']:
        path=downloads/item['url'].rsplit('/',1)[-1];fetch([item['url']],path,item['sha256'],item['bytes'])
        target=runtime if item['name']=='Python' else runtime/'packages'
        with zipfile.ZipFile(path) as archive:archive.extractall(target)
    (runtime/'python313._pth').write_text('python313.zip\n.\npackages\n',encoding='ascii')
    shutil.copy2(Path(__file__).with_name('ct2_worker.py'),runtime/'ct2_worker.py')
    for item in manifest['translation']['archives']:
        output=root/'models/translation'/item['pair']
        if all((output/name).is_file() and digest(output/name)==record['sha256'] for name,record in item['files'].items()):continue
        path=downloads/(item['pair']+'-opus-2020-07-17.zip');fetch([item['url']],path,item['sha256'],item['bytes'])
        original=downloads/item['pair'];original.mkdir(exist_ok=True)
        with zipfile.ZipFile(path) as archive:
            for entry in archive.infolist():
                name=Path(entry.filename)
                if name.is_absolute() or '..' in name.parts:raise ValueError('Invalid model archive path')
            archive.extractall(original)
        subprocess.run([str(runtime/'python.exe'),'-I','-B',str(Path(__file__).with_name('convert_opus.py')),'--source',str(original),'--output',str(output)],check=True)
        for name,record in item['files'].items():
            if digest(output/name)!=record['sha256']:raise ValueError('Converted model does not match pinned output: '+name)
    print('CTranslate2 runtime and both INT8 models verified.',flush=True)
