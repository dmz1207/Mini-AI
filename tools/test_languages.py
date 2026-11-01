"""Real smoke translations for each advertised Hy-MT2 target."""
import argparse,json,time,urllib.request
from pathlib import Path
def run(root,output):
    state=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'));opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    def request(path,body=None):
        data=None if body is None else json.dumps(body,ensure_ascii=False).encode();headers={'Authorization':'Bearer '+state['token'],'Content-Type':'application/json'}
        with opener.open(urllib.request.Request(state['url']+path,data=data,headers=headers),timeout=120) as response:return json.load(response)
    health=request('/health');codes=health['languages'];assert len(codes)==38 and len(set(codes))==38
    records=[];ranges={'ja':(0x3040,0x9fff),'ko':(0xac00,0xd7af),'ru':(0x400,0x52f),'uk':(0x400,0x52f),'kk':(0x400,0x6ff),'mn':(0x400,0x52f),'ar':(0x600,0x6ff),'fa':(0x600,0x6ff),'ur':(0x600,0x6ff),'ug':(0x600,0x6ff),'he':(0x590,0x5ff),'hi':(0x900,0x97f),'mr':(0x900,0x97f),'bn':(0x980,0x9ff),'ta':(0xb80,0xbff),'te':(0xc00,0xc7f),'gu':(0xa80,0xaff),'th':(0xe00,0xe7f),'my':(0x1000,0x109f),'km':(0x1780,0x17ff),'bo':(0xf00,0xfff),'zh':(0x4e00,0x9fff),'zh-Hant':(0x4e00,0x9fff),'yue':(0x4e00,0x9fff)}
    for code in codes:
        start=time.perf_counter();result=request('/v1/translate',{'text':'谢谢。' if code=='en' else 'Thank you.','target':code,'mode':'fast'});value=result['text'];assert value and not result['truncated'] and result['target']==code,(code,result)
        script=True
        if code in ranges:
            low,high=ranges[code];script=any(low<=ord(c)<=high or code=='mn' and 0x1800<=ord(c)<=0x18af for c in value)
        row={'target':code,'text':value,'seconds':round(time.perf_counter()-start,3),'expected_script':script};records.append(row);print(json.dumps(row,ensure_ascii=True),flush=True)
    output.mkdir(parents=True,exist_ok=True);(output/'language-checks.json').write_text(json.dumps({'targets':len(records),'all_completed':True,'records':records},ensure_ascii=False,indent=2),encoding='utf-8')
    assert all(x['expected_script'] for x in records),'One or more language results need review'
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();run(a.root,a.output)
