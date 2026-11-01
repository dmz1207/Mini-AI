"""Exercise the dedicated formula endpoint using a rendered mathematical crop."""
import argparse,base64,json,re,time,urllib.request
from pathlib import Path

def run(root,image,output):
    state=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'));opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));data=json.dumps({'image_base64':base64.b64encode(image.read_bytes()).decode(),'formula_only':True}).encode()
    records=[]
    for n in range(2):
        started=time.perf_counter()
        request=urllib.request.Request(state['url']+'/v1/ocr',data=data,headers={'Content-Type':'application/json','Authorization':'Bearer '+state['token']})
        with opener.open(request,timeout=120) as response:value=json.load(response)
        assert value['model']=='RapidLaTeX OCR' and value['formula_count']==1 and value['lines'][0]['kind']=='formula',value
        tex=value['lines'][0]['tex'];compact=re.sub(r'\s+','',tex);assert r'e^{-t}' in compact and r'e^{-2t}' in compact,tex
        assert value['width']>100 and value['height']>30
        records.append({'seconds':round(time.perf_counter()-started,3),'tex':tex,'model':value['model'],'formula_count':value['formula_count']});print(json.dumps(records[-1],ensure_ascii=True),flush=True)
    output.mkdir(parents=True,exist_ok=True);(output/'formula-service-checks.json').write_text(json.dumps({'passed':6,'records':records},ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--image',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();run(a.root,a.image,a.output)
