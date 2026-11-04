"""Real-model checks for lazy formula loading, bounded reuse and concurrency."""
import argparse,base64,concurrent.futures,io,json,time,urllib.request
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont,PngImagePlugin

def run(root,output):
    state=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'));checks=[];timings=[]
    def request(path,body=None):
        opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
        req=urllib.request.Request(state['url']+path,data=None if body is None else json.dumps(body).encode(),headers={'Authorization':'Bearer '+state['token'],'Content-Type':'application/json'})
        with opener.open(req,timeout=120) as response:return json.load(response)
    def check(value,name):
        assert value,name
        checks.append(name);print('PASS:',name,flush=True)
    image=Image.new('RGB',(1100,170),'white');ImageDraw.Draw(image).text((32,45),'The control system remains stable.',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',44),fill='black')
    def body(n=0,**options):
        meta=PngImagePlugin.PngInfo();meta.add_text('test-case',str(n));data=io.BytesIO();image.save(data,format='PNG',pnginfo=meta)
        return {'image_base64':base64.b64encode(data.getvalue()).decode(),'formula':True,**options}
    check(not request('/health')['formula_loaded'],'startup leaves formula graphs unloaded')
    first=body();clock=time.perf_counter();reply=request('/v1/ocr',first);timings.append({'case':'first plain capture','seconds':time.perf_counter()-clock})
    check('control system' in reply['text'] and reply['formula_count']==0,'automatic OCR retains ordinary prose')
    check(not request('/health')['formula_loaded'],'ordinary prose never initializes the formula models')
    before=request('/health');clock=time.perf_counter();again=request('/v1/ocr',first);timings.append({'case':'cached capture','seconds':time.perf_counter()-clock});after=request('/health')
    check(again==reply,'an exact repeated image retains every character, score and coordinate')
    check(after['ocr_inferences']==before['ocr_inferences'] and after['ocr_cache_hits']==before['ocr_cache_hits']+1,'repeated images bypass native model execution')
    text=request('/v1/ocr',body(formula=False));check(text['formula'] is False and text['formula_count']==0,'cache keys separate text and automatic formula modes')
    fast=request('/v1/ocr',body(1,mode='fast'));check(fast['mode']=='fast' and 'control system' in fast['text'],'cache keys retain the requested OCR quality')
    duplicate=body(2,mode='fast');before=request('/health')
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results=list(pool.map(lambda _:request('/v1/ocr',duplicate),range(2)))
    after=request('/health');check(results[0]==results[1],'simultaneous identical crops receive the same complete result')
    check(after['ocr_inferences']==before['ocr_inferences']+1,'simultaneous identical crops run the model once')
    for n in range(3,8):request('/v1/ocr',body(n,mode='fast'))
    before=request('/health');request('/v1/ocr',duplicate);after=request('/health')
    check(after['ocr_inferences']==before['ocr_inferences']+1,'the bounded cache evicts old image replies')
    check(not (root/'MiniAI Results').exists(),'instant recognition does not create result files')
    output.mkdir(parents=True,exist_ok=True);(output/'ocr-cache-checks.json').write_text(json.dumps({'passed':len(checks),'checks':checks,'timings':timings},indent=2),encoding='utf-8')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args();run(a.root,a.output)
