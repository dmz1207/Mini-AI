"""Launch only the distributed EXE in an empty directory and a system-only PATH."""
import argparse,base64,hashlib,json,os,shutil,subprocess,time,urllib.request,zipfile
from pathlib import Path
from check_dependencies import check

def run(exe,output):
    exe=Path(exe).resolve();output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    clean=output/'portable folder 空格';clean.mkdir(exist_ok=True);launcher=clean/'MiniAI-Portable.exe'
    shutil.copy2(exe,launcher);root=clean/'MiniAI';checks=[]
    def passed(condition,label):
        if not condition:raise AssertionError(label)
        checks.append(label);print('PASS:',label,flush=True)
    env=dict(os.environ,PATH=os.path.join(os.environ['WINDIR'],'System32')+';'+os.environ['WINDIR'],TEMP=str(clean),TMP=str(clean))
    started=time.perf_counter();process=subprocess.Popen([str(launcher),'--headless','--port','0'],cwd=clean,env=env,creationflags=0x08000000)
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));connection=None
    def request(path,body=None):
        data=None if body is None else json.dumps(body,ensure_ascii=False).encode('utf-8')
        req=urllib.request.Request(connection['url']+path,data=data,headers={'Content-Type':'application/json','Authorization':'Bearer '+connection['token']})
        with opener.open(req,timeout=240) as reply:return json.loads(reply.read())
    try:
        while time.perf_counter()-started<120:
            try:
                connection=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'))
                health=request('/health')
                if health.get('ok'):break
            except (OSError,ValueError):pass
            if process.poll() is not None:raise AssertionError('Single EXE startup failed; see MiniAI-portable-error.txt')
            time.sleep(.1)
        else:raise AssertionError('Single EXE startup timed out')
        cold=round(time.perf_counter()-started,3)
        passed(health['ocr'] and health['translation'],'empty-folder first run extracts and enables both models with system-only PATH')
        with zipfile.ZipFile(launcher) as archive:manifest=json.loads(archive.read('MiniAI/package-checksums.json'))
        passed(all((root/name).is_file() and hashlib.file_digest((root/name).open('rb'),'sha256').hexdigest()==record['sha256'] for name,record in manifest.items()),'every extracted file matches the embedded package manifest')
        check([p for p in root.rglob('*') if p.suffix.lower() in ('.exe','.dll')]);passed(True,'every native dependency is bundled or a Windows system DLL')
        a=request('/v1/translate',{'text':'The control system remains stable.','target':'zh'})
        b=request('/v1/translate',{'text':'该系统保持稳定。','target':'en'})
        passed('控制' in a['text'] and '稳定' in a['text'] and not a['truncated'],'single EXE actual English to Chinese translation')
        passed('system' in b['text'].lower() and 'stable' in b['text'].lower() and not b['truncated'],'single EXE actual Chinese to English translation')
        from PIL import Image,ImageDraw,ImageFont
        image=Image.new('RGB',(1400,240),'white');draw=ImageDraw.Draw(image);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',46)
        draw.text((40,70),'The control system remains stable.',font=font,fill='black');photo=output/'photo.jpg';image.save(photo,quality=94)
        result=request('/v1/ocr',{'image_base64':base64.b64encode(photo.read_bytes()).decode()})
        passed('control system' in result['text'],'single EXE actual photo OCR with bundled OCR dependencies')
    finally:
        if connection:
            try:request('/v1/shutdown',{})
            except OSError:pass
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.terminate();process.wait(timeout=5)
    model=root/'models/translation/hy-mt2.gguf';stamp=model.stat().st_mtime_ns
    user=root/'my-notes.txt';user.write_text('keep my notes',encoding='utf-8')
    # Only deliberately damage owned test copies inside the isolated output folder.
    crt=root/'runtime/msvcp140_1.dll';crt.unlink();engine=root/'runtime/translation/avx2/llama-server.exe'
    # Windows can release an image mapping shortly after its process handle signals.
    for attempt in range(40):
        try:
            with engine.open('r+b') as stream:stream.seek(-1,2);old=stream.read(1);stream.seek(-1,2);stream.write(bytes([old[0]^1]))
            break
        except PermissionError:
            if attempt==39:raise
            time.sleep(.1)
    warm=time.perf_counter();repair=subprocess.run([str(launcher),'--extract-only'],cwd=clean,env=env,timeout=120,creationflags=0x08000000)
    warm=round(time.perf_counter()-warm,3)
    passed(repair.returncode==0 and crt.is_file(),'single EXE automatically repairs a missing OCR DLL')
    passed(hashlib.sha256(engine.read_bytes()).hexdigest()==manifest['runtime/translation/avx2/llama-server.exe']['sha256'],'single EXE repairs a damaged translation executable')
    passed(model.stat().st_mtime_ns==stamp and user.read_text(encoding='utf-8')=='keep my notes','valid models are reused and unrelated user files preserved')
    passed(not any(root.rglob('*.part')),'no unfinished extraction files remain')
    record={'assertions':len(checks),'checks':checks,'cold_start_seconds':cold,'repair_seconds':warm,'exe_bytes':exe.stat().st_size}
    (output/'checks.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8');print(json.dumps(record))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--exe',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();run(args.exe,args.output)
