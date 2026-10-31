"""Local integration checks; optional Pillow/PyMuPDF only generate test images.

No document contents or connection tokens are uploaded. Run from the package.
"""
import argparse,base64,json,re,socket,subprocess,time,urllib.request,urllib.error
from pathlib import Path

def run(root,output,translation=False):
    root=Path(root).resolve();output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));checks=[]
    def check(ok,name):
        if not ok:raise AssertionError(name)
        checks.append(name);print('PASS:',name,flush=True)
    def state():return json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'))
    def request(path,body=None,headers=None,token=True):
        info=state();h={'Content-Type':'application/json'}
        if token:h['Authorization']='Bearer '+info['token']
        if headers:h.update(headers)
        payload=None if body is None else json.dumps(body,ensure_ascii=False).encode('utf-8')
        try:
            with opener.open(urllib.request.Request(info['url']+path,data=payload,headers=h),timeout=660) as reply:
                return reply.status,json.loads(reply.read()),dict(reply.headers)
        except urllib.error.HTTPError as error:return error.code,json.loads(error.read()),dict(error.headers)
    def ocr(image,label):
        start=time.perf_counter();code,result,_=request('/v1/ocr',{'image_base64':base64.b64encode(image.read_bytes()).decode()})
        check(code==200,label+' completes');result['seconds']=round(time.perf_counter()-start,3)
        (output/(label+'.json')).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8');return result
    check(request('/health')[1].get('name')=='MiniAI','authenticated health endpoint')
    check(request('/health',token=False)[0]==401,'reject unauthenticated requests')
    check(request('/health',headers={'Authorization':'Bearer wrong'})[0]==401,'reject invalid token')
    check(request('/health',headers={'Host':'example.com'})[0]==403,'reject nonlocal Host header')
    check(request('/health',headers={'Origin':'https://untrusted.invalid'})[0]==403,'deny browser origins by default')
    allowed=root/'allowed-origins.txt';old=allowed.read_bytes() if allowed.exists() else None
    try:
        allowed.write_text('chrome-extension://miniai-test\n',encoding='utf-8')
        code,_,headers=request('/health',headers={'Origin':'chrome-extension://miniai-test'})
        check(code==200 and headers.get('Access-Control-Allow-Origin')=='chrome-extension://miniai-test','opt-in browser origin with authentication')
    finally:
        if old is None:allowed.unlink()
        else:allowed.write_bytes(old)
    check(request('/v1/translate',{'text':'test','target':'xx'})[0]==400,'reject unsupported translation target')
    check(request('/v1/ocr',{'image_base64':'invalid'})[0]==400,'invalid image does not crash service')
    before=state();child=subprocess.Popen([str(root/'MiniAI.exe'),'--headless','--port','0'],cwd=root,creationflags=0x08000000)
    check(child.wait(timeout=10)==0 and state()['pid']==before['pid'],'repeat launch reuses one service per package')
    from PIL import Image,ImageDraw,ImageFont,ImageEnhance
    image=Image.new('RGB',(1800,700),'white');draw=ImageDraw.Draw(image)
    en=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',48);zh=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',48)
    for x,y,text,font in [(70,70,'The control system remains stable.',en),(70,160,'Offline OCR and Chinese-English translation.',en),(70,290,'离线文字识别与中英文翻译。',zh),(70,390,'多智能体系统的一致性控制。',zh),(70,510,'Equation (12): x = 1.25, y = 0.5',en)]:draw.text((x,y),text,font=font,fill='black')
    path=output/'ocr-bilingual.png';image.save(path);result=ocr(path,'bilingual')
    check('control system remains stable' in result['text'] and '中英文翻译' in result['text'],'Chinese and English printed text')
    clipped=output/'edge-crop.png';image.crop((50,50,1250,210)).save(clipped);result=ocr(clipped,'edge-crop')
    check('control system' in result['text'],'screen crop recognizes text near its boundary')
    check(all(0<=p[0]<=1 and 0<=p[1]<=1 for line in result['lines'] for p in line['box']),'OCR coordinates remain bounded when a text box crosses the crop edge')
    tilted=output/'tilted.png';image.rotate(4,expand=True,fillcolor='white').save(tilted);result=ocr(tilted,'tilted')
    check('control system' in result['text'] and '中英文翻译' in result['text'],'four degree skew')
    low=output/'low-contrast.png';ImageEnhance.Contrast(image.resize((900,350))).enhance(.45).save(low);result=ocr(low,'low-contrast')
    check('control system' in result['text'] and '中英文翻译' in result['text'],'reduced resolution and contrast')
    columns=Image.new('RGB',(2200,1700),'white');draw=ImageDraw.Draw(columns);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',42)
    draw.text((300,50),'Experimental results and discussion',font=font,fill='black')
    for i in range(10):
        draw.text((80,200+i*125),'Left column line %02d stability'%(i+1),font=font,fill='black')
        draw.text((1180,200+i*125),'Right column line %02d control'%(i+1),font=font,fill='black')
    colpath=output/'columns.png';columns.save(colpath);result=ocr(colpath,'columns');texts=[line['text'].lower() for line in result['lines']]
    left=[i for i,t in enumerate(texts) if 'left column' in t];right=[i for i,t in enumerate(texts) if 'right column' in t]
    check(len(left)>=9 and len(right)>=9 and max(left)<min(right),'two-column reading order follows each column')
    # Independently parse the layer produced by the Mini PDF integration test.
    pdf=root.parent/'work/offline-tests/searchable-bilingual.pdf'
    if pdf.exists():
        import fitz
        with fitz.open(pdf) as document:
            text=document[0].get_text();check('control system' in text and '中英文翻译' in text,'saved OCR layer is readable by an independent PDF engine')
            document[0].get_pixmap(matrix=fitz.Matrix(1.2,1.2)).save(output/'independent-layer.png')
    if translation:
        cases=[('The control system remains stable.','zh'),('该系统保持稳定。','en'),('The proposed event-triggered control protocol guarantees consensus of all agents. The simulation results verify the effectiveness of the method.','zh'),('本文提出一种事件触发控制协议，保证所有智能体达到一致性。仿真结果验证了该方法的有效性。','en')]
        for i,(text,target) in enumerate(cases):
            start=time.perf_counter();code,result,_=request('/v1/translate',{'text':text,'target':target});duration=round(time.perf_counter()-start,3)
            check(code==200 and result.get('text') and not result.get('truncated'),'translation case %d completes without truncation'%(i+1))
            translated=result['text'];check(bool(re.search('[\u4e00-\u9fff]' if target=='zh' else '[A-Za-z]',translated)),'translation case %d uses the target language'%(i+1))
            check('Preserve equations' not in translated and '保留方程' not in translated,'translation case %d excludes prompt instructions'%(i+1))
            if i==0:check('控制' in translated and '稳定' in translated and len(translated)<80,'short Chinese translation preserves the meaning')
            if i==1:check('system' in translated.lower() and 'stable' in translated.lower() and len(translated)<100,'short English translation preserves the meaning')
            result.update(source=text,seconds=duration);(output/('translation-%d.json'%(i+1))).write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
            print('TRANSLATION:',translated.encode('unicode_escape').decode(),'SECONDS',duration,flush=True)
        start=time.perf_counter();_,cached,_=request('/v1/translate',{'text':cases[0][0],'target':'zh'})
        check(cached.get('cached') is True and time.perf_counter()-start<2,'repeated translation uses the shared in-memory result cache')
        for code,needle in [('de','stabil'),('fr','stable'),('ja','安定')]:
            _,reply,_=request('/v1/translate',{'text':'The control system remains stable.','target':code})
            check(needle in reply.get('text','').lower() and not reply.get('truncated'),'additional target '+code+' preserves the stability statement')
            (output/('translation-'+code+'.json')).write_text(json.dumps(reply,ensure_ascii=False,indent=2),encoding='utf-8')
        _,reply,_=request('/v1/translate',{'text':'The control system remains stable under bounded disturbances.','target':'zh'})
        check(reply.get('kv_cached_tokens',0)>0,'persistent native worker reuses the common prompt KV cache')
    (output/'checks.json').write_text(json.dumps({'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2),encoding='utf-8')
    print('Assertions:',len(checks),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--translation',action='store_true');args=parser.parse_args();run(args.root,args.output,args.translation)
