"""Exercise the actual authenticated streaming service, cancellation and caches."""
import argparse,json,os,socket,time,urllib.request
from pathlib import Path

def run(root,output):
    root=Path(root).resolve();state=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'))
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));checks=[];metrics=[]
    def check(ok,name):
        if not ok:raise AssertionError(name)
        checks.append(name);print('PASS:',name,flush=True)
    def translate(text,target='zh',mode='fast'):
        data=json.dumps({'text':text,'target':target,'mode':mode,'stream':True},ensure_ascii=False).encode('utf-8')
        request=urllib.request.Request(state['url']+'/v1/translate',data=data,headers={'Content-Type':'application/json','Authorization':'Bearer '+state['token']})
        started=time.perf_counter();events=[];first=None
        with opener.open(request,timeout=60) as response:
            check(response.headers.get_content_type()=='application/x-ndjson','endpoint streams newline-delimited JSON')
            for line in response:
                event=json.loads(line);events.append(event)
                if event.get('type')=='partial' and event.get('text') and first is None:first=time.perf_counter()-started
        duration=time.perf_counter()-started
        check(events[-1]['type']=='done' and events[-1]['result']['text'],'stream terminates with a complete result')
        check(first is not None,'nonempty translated text is streamed before completion')
        metrics.append({'source_characters':len(text),'target':target,'mode':mode,'first_text_seconds':round(first,4),'total_seconds':round(duration,4),'cached':events[-1]['result']['cached']})
        return events[-1]['result']
    cases=[('The control system remains stable.','zh'),('该系统保持稳定。','en'),('This paper proposes a nonlinear controller. The closed-loop system is stable under bounded external disturbances. Numerical simulations demonstrate the effectiveness of the proposed method.','zh')]
    for text,target in cases:
        result=translate(text,target);check(not result['truncated'],'test passage is not cut off')
        check(result['threads']==result['physical_cores'] and result['beam_size']==1,'inference uses physical cores and greedy decoding')
    original=translate(cases[0][0]);cached=translate(cases[0][0]);check(cached['cached'] and cached['text']==original['text'],'repeated text uses the result cache without changing its content')
    # Disconnect a long stream while the engine is generating. The next request
    # must not sit behind its entire abandoned paragraph.
    from urllib.parse import urlparse
    address=urlparse(state['url']);payload=json.dumps({'text':' '.join('Experimental case %d shows that the nonlinear control system remains stable under external disturbances.'%i for i in range(65)),'target':'zh','stream':True}).encode()
    with socket.create_connection((address.hostname,address.port),timeout=10) as connection:
        head=('POST /v1/translate HTTP/1.1\r\nHost: 127.0.0.1:%d\r\nAuthorization: Bearer %s\r\nContent-Type: application/json\r\nContent-Length: %d\r\nConnection: close\r\n\r\n'%(address.port,state['token'],len(payload))).encode()
        connection.sendall(head+payload);connection.recv(4096)
    started=time.perf_counter();latest=translate('The control system remains stable.')
    check(time.perf_counter()-started<4 and '稳定' in latest['text'],'abandoned long selection is canceled so the latest selection can finish promptly')
    fresh_quality=translate('The control system remains stable.','zh','quality');check(fresh_quality['beam_size']==4 and not fresh_quality['cached'],'quality mode uses beam 4 and has a separate cache')
    quality=translate('The control system remains stable.','zh','quality');check(quality['cached'] and quality['text']==fresh_quality['text'],'quality mode caches its own completed results')
    fast=translate('The control system remains stable.');check(fast['beam_size']==1 and fast['cached'],'switching back restores fast mode and its cache')
    (Path(output)/'streaming-checks.json').write_text(json.dumps({'assertions':len(checks),'checks':checks,'metrics':metrics},ensure_ascii=False,indent=2),encoding='utf-8')
    print('Streaming assertions:',len(checks),json.dumps(metrics),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True);run(args.root,args.output)
