"""Exercise the actual authenticated streaming service, cancellation and caches."""
import argparse,ctypes,json,os,socket,time,urllib.request
from pathlib import Path

def run(root,output):
    root=Path(root).resolve();state=json.loads((root/'state/connection.json').read_text(encoding='utf-8-sig'))
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));checks=[];metrics=[]
    def request(path,payload=None):
        data=None if payload is None else json.dumps(payload,ensure_ascii=False).encode('utf-8')
        headers={'Authorization':'Bearer '+state['token']}
        if data is not None:headers['Content-Type']='application/json'
        req=urllib.request.Request(state['url']+path,data=data,headers=headers)
        with opener.open(req,timeout=30) as response:return response.status,json.loads(response.read().decode('utf-8'))
    def check(ok,name):
        if not ok:raise AssertionError(name)
        checks.append(name);print('PASS:',name,flush=True)
    class FILETIME(ctypes.Structure):_fields_=[('low',ctypes.c_uint32),('high',ctypes.c_uint32)]
    def cpu_ticks(pid):
        if not pid:return None
        kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.OpenProcess.argtypes=[ctypes.c_uint32,ctypes.c_bool,ctypes.c_uint32];kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,ctypes.POINTER(FILETIME),ctypes.POINTER(FILETIME),ctypes.POINTER(FILETIME),ctypes.POINTER(FILETIME)];kernel.CloseHandle.argtypes=[ctypes.c_void_p];handle=kernel.OpenProcess(0x1000,False,pid)
        if not handle:return None
        created=FILETIME();exited=FILETIME();system=FILETIME();user=FILETIME()
        try:
            if not kernel.GetProcessTimes(handle,ctypes.byref(created),ctypes.byref(exited),ctypes.byref(system),ctypes.byref(user)):return None
            return (system.high<<32|system.low)+(user.high<<32|user.low)
        finally:kernel.CloseHandle(handle)
    def translate(text,target='zh',mode='fast'):
        before_health=request('/health')[1].get('inference',{});before_pid=before_health.get('inference_pid',0);before_cpu=cpu_ticks(before_pid)
        data=json.dumps({'text':text,'target':target,'mode':mode,'stream':True},ensure_ascii=False).encode('utf-8')
        http_request=urllib.request.Request(state['url']+'/v1/translate',data=data,headers={'Content-Type':'application/json','Authorization':'Bearer '+state['token']})
        started=time.perf_counter();events=[];first=None
        with opener.open(http_request,timeout=60) as response:
            check(response.headers.get_content_type()=='application/x-ndjson','endpoint streams newline-delimited JSON')
            for line in response:
                event=json.loads(line);events.append(event)
                if event.get('type')=='partial' and event.get('text') and first is None:first=time.perf_counter()-started
        duration=time.perf_counter()-started
        check(events[-1]['type']=='done' and events[-1]['result']['text'],'stream terminates with a complete result')
        check(first is not None,'nonempty translated text is streamed before completion')
        after_health=request('/health')[1].get('inference',{});after_pid=after_health.get('inference_pid',0);after_cpu=cpu_ticks(after_pid)
        core_equivalents=None
        if before_cpu is not None and after_cpu is not None and before_pid==after_pid and duration>0:core_equivalents=round((after_cpu-before_cpu)/10000000/duration,2)
        metrics.append({'source_characters':len(text),'target':target,'mode':mode,'first_text_seconds':round(first,4),'total_seconds':round(duration,4),'cpu_core_equivalents':core_equivalents,'cpu_threads':events[-1]['result'].get('threads'),'physical_cores':events[-1]['result'].get('physical_cores'),'backend':events[-1]['result'].get('backend'),'cached':events[-1]['result']['cached']})
        return events[-1]['result']
    cases=[('The control system remains stable.','zh'),('该系统保持稳定。','en'),('This paper proposes a nonlinear controller. The closed-loop system is stable under bounded external disturbances. Numerical simulations demonstrate the effectiveness of the proposed method.','zh')]
    for text,target in cases:
        result=translate(text,target);check(not result['truncated'],'test passage is not cut off')
        check(result['threads']>=result['physical_cores'] and result['threads']<=result['logical_threads'] and result['beam_size']==1,'inference uses CPU threads and greedy decoding')
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
    fresh_quality=translate('The control system remains stable.','zh','quality');check(fresh_quality['engine']=='hy' and fresh_quality['mode']=='quality' and fresh_quality['decode_strategy']=='sampling' and not fresh_quality['cached'],'Hy high-quality mode uses its separate cache')
    quality=translate('The control system remains stable.','zh','quality');check(quality['cached'] and quality['mode']=='quality' and quality['text']==fresh_quality['text'],'quality mode caches its own completed results')
    fast=translate('The control system remains stable.');check(fast['beam_size']==1 and fast['decode_strategy']=='greedy' and fast['cached'],'fast mode uses greedy decoding and its own cache')
    (Path(output)/'streaming-checks.json').write_text(json.dumps({'assertions':len(checks),'checks':checks,'metrics':metrics},ensure_ascii=False,indent=2),encoding='utf-8')
    print('Streaming assertions:',len(checks),json.dumps(metrics),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True);run(args.root,args.output)
