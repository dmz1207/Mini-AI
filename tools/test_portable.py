"""Test the distributed EXE, repair, sharing, persistent results and exit cleanup."""
import argparse,base64,ctypes,hashlib,json,os,shutil,subprocess,threading,time,urllib.request,zipfile
from pathlib import Path
from check_dependencies import check

def run(exe,output):
    exe=Path(exe).resolve();output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    home=output/'portable folder 空格';home.mkdir(exist_ok=True);launcher=home/'MiniAI-Portable.exe';shutil.copy2(exe,launcher);root=home/'MiniAI';checks=[]
    env=dict(os.environ,PATH=os.environ['WINDIR']+'/System32;'+os.environ['WINDIR'],TEMP=str(home),TMP=str(home))
    kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));connection=None
    def passed(value,name):
        if not value:raise AssertionError(name)
        checks.append(name);print('PASS:',name,flush=True)
    def request(path,body=None):
        data=None if body is None else json.dumps(body,ensure_ascii=False).encode('utf-8')
        req=urllib.request.Request(connection['url']+path,data=data,headers={'Content-Type':'application/json','Authorization':'Bearer '+connection['token']})
        with opener.open(req,timeout=90) as response:return json.loads(response.read())
    def start():
        nonlocal connection
        process=subprocess.Popen([str(launcher),'--headless','--port','0'],cwd=home,env=env,creationflags=0x08000000);clock=time.perf_counter()
        for _ in range(1200):
            try:
                connection=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'));health=request('/health')
                if health['ok']:return process,time.perf_counter()-clock,health
            except (OSError,ValueError):pass
            if process.poll() is not None:raise AssertionError('Portable startup failed')
            time.sleep(.1)
        raise AssertionError('Portable startup timed out')
    with zipfile.ZipFile(launcher) as archive:manifest=json.loads(archive.read('MiniAI/package-checksums.json'))
    # The explicit developer extraction command intentionally keeps files for repair.
    prepare=subprocess.run([str(launcher),'--extract-only'],cwd=home,env=env,timeout=120,creationflags=0x08000000)
    passed(prepare.returncode==0,'developer extraction succeeds without Python on PATH')
    passed(all((root/name).is_file() and hashlib.sha256((root/name).read_bytes()).hexdigest()==value['sha256'] for name,value in manifest.items()),'every payload file matches its embedded SHA-256')
    check([p for p in root.rglob('*') if p.suffix.lower() in ('.exe','.dll','.pyd')]);passed(True,'EXE, DLL and PYD dependency closure is complete')
    model=root/'models/translation/hy-mt2.gguf';stamp=model.stat().st_mtime_ns;crt=root/'runtime/msvcp140_1.dll';crt.unlink();engine=root/'models/formula/encoder.onnx'
    with engine.open('r+b') as stream:stream.seek(-1,2);value=stream.read(1);stream.seek(-1,2);stream.write(bytes([value[0]^1]))
    repair=subprocess.run([str(launcher),'--extract-only'],cwd=home,env=env,timeout=120,creationflags=0x08000000)
    passed(repair.returncode==0 and crt.exists(),'missing OCR dependency is repaired')
    passed(hashlib.sha256(engine.read_bytes()).hexdigest()==manifest['models/formula/encoder.onnx']['sha256'],'damaged formula OCR model is repaired')
    passed(model.stat().st_mtime_ns==stamp,'verified models are reused within an extraction session')
    process,cold,health=start();saved=[];delayed=None
    try:
        passed(health['ocr'] and health['translation'] and health.get('formula'),'Hy-MT2, text OCR and formula OCR run from an isolated Unicode folder and system-only PATH')
        a=request('/v1/translate',{'text':'The control system remains stable.','target':'zh','mode':'fast'})
        b=request('/v1/translate',{'text':'该系统保持稳定。','target':'en','mode':'quality'})
        passed('控制' in a['text'] and '稳定' in a['text'] and a['beam_size']==1,'actual fast English-to-Chinese translation')
        passed('system' in b['text'].lower() and 'stable' in b['text'].lower() and b['beam_size']==1,'actual quality Chinese-to-English translation')
        from PIL import Image,ImageDraw,ImageFont
        image=Image.new('RGB',(1400,240),'white');ImageDraw.Draw(image).text((40,70),'The control system remains stable.',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',46),fill='black');photo=output/'photo.jpg';image.save(photo,quality=94)
        result=request('/v1/ocr',{'image_base64':base64.b64encode(photo.read_bytes()).decode()});passed('control system' in result['text'],'actual photo OCR uses bundled dependencies')
        formula=request('/v1/ocr',{'image_base64':base64.b64encode(photo.read_bytes()).decode(),'formula':True});passed(formula.get('formula') is True and any(line.get('kind')=='text' for line in formula.get('lines',[])),'formula-aware OCR keeps ordinary text classified as text')
        folder=home/'MiniAI Results';passed(not folder.exists(),'instant API OCR and translation leave no result files')
        for kind,text,source in [('ocr',result['text'],''),('translation',a['text'],'The control system remains stable.')]:
            value=request('/v1/results',{'kind':kind,'text':text,'source':source,'target':'zh'});path=Path(value['file']);saved.append((path,path.read_bytes()));passed(path.parent==folder and path.is_file(),'regular '+kind+' result is saved outside the temporary runtime')
    finally:
        # A temporary scanner/file handle must not leave an owned executable behind.
        held=kernel.CreateFileW(str(root/'runtime/translation/baseline/llama-server.exe'),0x80000000,3,None,3,0,None)
        if held==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
        delayed=threading.Timer(3.5,lambda:kernel.CloseHandle(held));delayed.start()
        try:request('/v1/shutdown',{})
        except OSError:pass
        process.wait(timeout=60)
        delayed.join(timeout=10)
    passed(not root.exists(),'normal exit removes the extracted models, runtime, credentials and temporary folder')
    passed(not delayed.is_alive(),'cleanup waits for a transient native-file lock to be released')
    passed(all(path.read_bytes()==value for path,value in saved),'OCR and translation results survive runtime cleanup')
    passed(launcher.is_file(),'the standalone EXE is retained')
    # Share the service through a real OS file lease, then release the final client.
    process,_,_=start();unknown=root/'my-notes.txt';unknown.write_text('user file must survive',encoding='utf-8')
    leases=root/'state/clients';leases.mkdir(exist_ok=True);path=leases/'123456-0123456789abcdef0123456789abcdef.lock'
    kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p,ctypes.c_uint,ctypes.c_uint,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    lease=kernel.CreateFileW(str(path),0xC0000000,0,None,1,0x04000000,None)
    if lease==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
    try:
        request('/v1/client-release',{});time.sleep(1.3);passed(request('/health')['ok'] and process.poll() is None,'another active client prevents premature service shutdown and cleanup')
    finally:kernel.CloseHandle(lease)
    process.wait(timeout=60)
    passed(unknown.read_text(encoding='utf-8')=='user file must survive','cleanup preserves unrecognized user files')
    passed(not (root/'models').exists() and not (root/'runtime').exists(),'the final client release cleans owned model and runtime files')
    passed(not any(root.rglob('*.part')),'no unfinished extraction files remain')
    unknown.unlink();root.rmdir()
    record={'assertions':len(checks),'checks':checks,'startup_seconds':round(cold,3),'exe_bytes':exe.stat().st_size,'auto_cleanup':True,'instant_results_saved':False}
    (output/'checks.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8');print(json.dumps(record),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--exe',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();run(args.exe,args.output)
