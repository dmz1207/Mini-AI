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
    def ocr(image,label,mode='quality'):
        start=time.perf_counter();code,result,_=request('/v1/ocr',{'image_base64':base64.b64encode(image.read_bytes()).decode(),'mode':mode})
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
    path=output/'ocr-bilingual.png';image.save(path)
    exif=Image.Exif();exif[274]=6
    image.transpose(Image.Transpose.ROTATE_90).save(output/'photo-oriented.jpg',quality=95,exif=exif)
    result=ocr(path,'bilingual')
    check('control system remains stable' in result['text'] and '中英文翻译' in result['text'],'Chinese and English printed text')
    check(result['mode']=='quality' and result['model']=='PP-OCRv6 medium','PDF/default-quality OCR retains the medium model')
    check(request('/health')[1].get('ocr_fast') is True,'fast OCR capability is reported')
    fast=ocr(path,'fast-bilingual','fast')
    check('control system remains stable' in fast['text'] and '中英文翻译' in fast['text'],'tiny OCR recognizes the bilingual fast fixture')
    check(fast['mode']=='fast' and fast['model']=='PP-OCRv6 tiny','fast requests report the tiny model actually used')
    check(all(0<=p[0]<=1 and 0<=p[1]<=1 for line in fast['lines'] for p in line['box']),'fast OCR coordinates are bounded')
    check(request('/v1/ocr',{'image_base64':'invalid','mode':'unsupported'})[0]==400,'invalid OCR modes are rejected')
    clipped=output/'edge-crop.png';image.crop((50,50,1250,210)).save(clipped);result=ocr(clipped,'edge-crop')
    check('control system' in result['text'],'screen crop recognizes text near its boundary')
    check(all(0<=p[0]<=1 and 0<=p[1]<=1 for line in result['lines'] for p in line['box']),'OCR coordinates remain bounded when a text box crosses the crop edge')
    tilted=output/'tilted.png';image.rotate(4,expand=True,fillcolor='white').save(tilted);result=ocr(tilted,'tilted')
    check('control system' in result['text'] and '中英文翻译' in result['text'],'four degree skew')
    low=output/'low-contrast.png';ImageEnhance.Contrast(image.resize((900,350))).enhance(.45).save(low);result=ocr(low,'low-contrast')
    check('control system' in result['text'] and '中英文翻译' in result['text'],'reduced resolution and contrast')
    formula_image=Image.new('RGB',(1500,600),'white');formula_draw=ImageDraw.Draw(formula_image);formula_font=ImageFont.truetype('C:/Windows/Fonts/cambria.ttc',100,index=0);formula_small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',56)
    formula_draw.text((70,35),'The system is stable.',font=formula_small,fill='black');formula_draw.text((80,175),'x² + y² = z²',font=formula_font,fill='black');formula_draw.text((900,190),'E = mc²',font=formula_font,fill='black');formula_draw.text((650,355),'a',font=formula_font,fill='black');formula_draw.line((580,465,755,465),fill='black',width=7);formula_draw.text((650,470),'b',font=formula_font,fill='black');formula_path=output/'formula-layout.png';formula_image.save(formula_path)
    code,formula,_=request('/v1/ocr',{'image_base64':base64.b64encode(formula_path.read_bytes()).decode(),'mode':'quality','formula':True})
    check(code==200 and formula.get('formula') is True,'formula-aware screenshot uses the bundled math recognizer')
    check(any(line.get('kind')=='text' and 'system is stable' in line.get('text','') for line in formula['lines']),'formula classification keeps ordinary prose as text')
    check(any(line.get('kind')=='formula' and line.get('tex')=='x^{2} + y^{2} = z^{2}' for line in formula['lines']),'superscripts and equation operators are preserved in TeX')
    check('\\frac{a}{b}' in formula.get('text',''),'stacked numerator and denominator become a TeX fraction')
    (output/'formula-layout-result.json').write_text(json.dumps(formula,ensure_ascii=False,indent=2),encoding='utf-8')
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
        for code,needle in [('zh-Hant','穩定')]:
            _,reply,_=request('/v1/translate',{'text':'The control system remains stable.','target':code})
            check(needle in reply.get('text','').lower() and not reply.get('truncated'),'additional target '+code+' preserves the stability statement')
            (output/('translation-'+code+'.json')).write_text(json.dumps(reply,ensure_ascii=False,indent=2),encoding='utf-8')
        _,health,_=request('/health')
        check(health['inference']['warmed'] and health['inference']['beam_size']==1,'persistent worker is prewarmed and uses greedy decoding')
    check(health['inference']['threads']>=health['inference']['physical_cores'],'CPU inference uses the physical-core budget or an explicit thread override')
    (output/'checks.json').write_text(json.dumps({'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2),encoding='utf-8')
    print('Assertions:',len(checks),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1]);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--translation',action='store_true');args=parser.parse_args();run(args.root,args.output,args.translation)
