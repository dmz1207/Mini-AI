"""Private, persistent CTranslate2 worker. UTF-8 JSON lines on inherited pipes.

No network, shell commands, user-supplied model paths, or runtime downloads.
"""
import argparse,collections,ctypes,glob,json,os,queue,re,sys,threading,time
from pathlib import Path

def physical_cores():
    """Count processor-core records across all Windows processor groups."""
    if os.name=='nt':
        size=ctypes.c_ulong();fn=ctypes.windll.kernel32.GetLogicalProcessorInformationEx
        fn(0,None,ctypes.byref(size))
        if 0<size.value<1024*1024:
            buffer=ctypes.create_string_buffer(size.value)
            if fn(0,buffer,ctypes.byref(size)):
                offset=count=0
                while offset+8<=size.value:
                    relationship=ctypes.c_ulong.from_buffer(buffer,offset).value
                    length=ctypes.c_ulong.from_buffer(buffer,offset+4).value
                    if length<8 or offset+length>size.value:break
                    count+=relationship==0;offset+=length
                if count:return count
    return max(1,os.cpu_count() or 1)

def nvidia_devices():
    """The CUDA driver ships with NVIDIA drivers; CUDA toolkit DLLs are optional."""
    result=[]
    try:
        driver=ctypes.WinDLL('nvcuda.dll') if os.name=='nt' else ctypes.CDLL('libcuda.so.1')
        count=ctypes.c_int()
        if driver.cuInit(0)!=0 or driver.cuDeviceGetCount(ctypes.byref(count))!=0:return result
        for index in range(count.value):
            name=ctypes.create_string_buffer(256);memory=ctypes.c_size_t()
            if driver.cuDeviceGetName(name,256,index)!=0:continue
            driver.cuDeviceTotalMem_v2(ctypes.byref(memory),index)
            result.append({'index':index,'name':name.value.decode('utf-8','replace'),'vram':memory.value})
    except (OSError,AttributeError):pass
    return sorted(result,key=lambda device:device['vram'],reverse=True)

class Cancelled(Exception):pass

def traditional(text):
    if not text or os.name!='nt':return text
    fn=ctypes.windll.kernel32.LCMapStringEx
    fn.argtypes=[ctypes.c_wchar_p,ctypes.c_uint,ctypes.c_wchar_p,ctypes.c_int,ctypes.c_wchar_p,ctypes.c_int,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_ssize_t]
    length=len(text.encode('utf-16-le'))//2
    size=fn('zh-TW',0x04000000,text,length,None,0,None,None,0)
    if size<=0:return text
    buffer=ctypes.create_unicode_buffer(size+1)
    return buffer.value if fn('zh-TW',0x04000000,text,length,buffer,size,None,None,0)>0 else text

class Engine:
    def __init__(self,root,threads):
        self.root=Path(root);self.threads=threads;self.models={};self.tokenizers={};self.backends={}
        self.cache=collections.OrderedDict();self.cache_chars=0;self.gpu=nvidia_devices()
        self.details={'engine':'CTranslate2','threads':threads,'physical_cores':physical_cores(),
                      'beam_size':1,'default_mode':'fast','modes':['fast','quality'],'cpu_isa':'auto','gpu_detected':bool(self.gpu),'backend':'cpu',
                      'compute_type':'int8','languages':['zh','en','zh-Hant'],'warmed':False}
        self.dll_handles=[]
        if os.name=='nt':
            directories=[self.root/'runtime',self.root/'runtime/cuda']
            if os.environ.get('CUDA_PATH'):directories.append(Path(os.environ['CUDA_PATH'])/'bin')
            directories+=map(Path,glob.glob(os.path.join(os.environ.get('ProgramFiles','C:/Program Files'),'NVIDIA GPU Computing Toolkit/CUDA/v*/bin')))
            for directory in directories:
                if directory.is_dir():
                    try:self.dll_handles.append(os.add_dll_directory(str(directory)))
                    except OSError:pass
        import ctranslate2,sentencepiece
        self.ct2=ctranslate2;self.spm=sentencepiece
        self.details['version']=ctranslate2.__version__

    def load(self,pair,cpu=False):
        if pair in self.models and not cpu:return self.models[pair]
        path=self.root/'models/translation'/pair
        # SentencePiece's Windows file loader cannot open every Unicode path.
        self.tokenizers[pair]=(self.spm.SentencePieceProcessor(model_proto=(path/'source.spm').read_bytes()),
                               self.spm.SentencePieceProcessor(model_proto=(path/'target.spm').read_bytes()))
        options=dict(inter_threads=1,intra_threads=self.threads,max_queued_batches=1)
        if self.gpu and not cpu:
            try:
                index=self.gpu[0]['index'];types=self.ct2.get_supported_compute_types('cuda',index)
                compute=next(t for t in ('int8_float16','int8_float32','float16','float32') if t in types)
                model=self.ct2.Translator(str(path),device='cuda',device_index=index,compute_type=compute,**options)
                # Exercise kernels now so missing cuBLAS DLLs trigger the CPU fallback.
                model.translate_batch([['Hello'] if pair=='eng-zho' else ['▁你']],beam_size=1,max_decoding_length=2)
                self.details.update(backend='cuda',compute_type=compute,gpu=self.gpu[0]['name'])
                self.models[pair]=model;self.backends[pair]={'backend':'cuda','compute_type':model.compute_type};self.update_backends();return model
            except Exception as error:
                self.details['gpu_fallback']=str(error)[:600]
        self.models[pair]=self.ct2.Translator(str(path),device='cpu',compute_type='int8',**options)
        self.backends[pair]={'backend':'cpu','compute_type':self.models[pair].compute_type};self.update_backends()
        return self.models[pair]

    def update_backends(self):
        devices={value['backend'] for value in self.backends.values()}
        self.details['backend']=next(iter(devices)) if len(devices)==1 else 'mixed'
        self.details['models_backend']=dict(self.backends)
        self.details['compute_type']=next(iter(self.backends.values()))['compute_type']

    def warm(self):
        for pair,text in (('eng-zho','Hello.'),('zho-eng','你好。')):
            model=self.load(pair);tokens=self.tokenizers[pair][0].encode(text,out_type=str)
            if pair=='eng-zho':tokens.insert(0,'>>cmn_Hans<<')
            model.translate_batch([tokens],beam_size=1,max_decoding_length=8,return_scores=False)
        self.details['warmed']=True

    def remember(self,key,value):
        if key in self.cache:self.cache_chars-=len(self.cache.pop(key))
        self.cache[key]=value;self.cache_chars+=len(value)
        while len(self.cache)>512 or self.cache_chars>300000:
            _,old=self.cache.popitem(last=False);self.cache_chars-=len(old)

    def segments(self,text,tokenizer):
        # Respect paragraph boundaries; wrap long clauses at word/token boundaries.
        text=re.sub(r'(?<=[A-Za-z])-\r?\n(?=[a-z])','',text)
        for paragraph in re.split(r'\r?\n\s*\r?\n',text):
            paragraph=re.sub(r'\s+',' ',paragraph).strip()
            if not paragraph:continue
            clauses=re.split(r'(?<=[。！？!?；;])\s*|(?<=[.])\s+(?=[A-Z\u4e00-\u9fff])',paragraph)
            for clause in clauses:
                if not clause:continue
                tokens=tokenizer.encode(clause,out_type=str)
                while len(tokens)>192:
                    cut=192
                    for index in range(191,100,-1):
                        if tokens[index].startswith('▁'):cut=index;break
                    yield tokens[:cut],'';tokens=tokens[cut:]
                if tokens:yield tokens,' '
            yield None,'\n\n'

    def translate(self,text,target,cancel,progress,mode='fast'):
        if target not in ('zh','en','zh-Hant'):raise ValueError('This lightweight pack supports Chinese and English.')
        if not text.strip() or len(text)>10000:raise ValueError('Provide 1 to 10000 characters.')
        if mode not in ('fast','quality'):raise ValueError('Unsupported translation mode.')
        beam=1 if mode=='fast' else 4;self.details.update(beam_size=beam,mode=mode)
        if target=='zh-Hant':
            original_progress=progress;progress=lambda value:original_progress(traditional(value))
        cache_key=(mode,target,text)
        if cache_key in self.cache:
            value=self.cache[cache_key];self.cache.move_to_end(cache_key);progress(value)
            return dict(text=value,target=target,model='OPUS-MT INT8',truncated=False,cached=True,**self.details)
        pair='zho-eng' if target=='en' else 'eng-zho';model=self.load(pair)
        src,tgt=self.tokenizers[pair];pieces=[];truncated=False;last_emit=0
        for tokens,separator in self.segments(text,src):
            if cancel.is_set():raise Cancelled()
            if tokens is None:
                if pieces:pieces[-1]=pieces[-1].rstrip()+'\n\n'
                continue
            separator=' ' if target=='en' else ''
            key=(mode,target,tuple(tokens));previous=''.join(pieces);output=[]
            if key in self.cache:
                value=self.cache[key];self.cache.move_to_end(key);pieces.append(value+separator);progress(''.join(pieces).rstrip());continue
            source=tokens if pair=='zho-eng' else [('>>cmn_Hant<<' if target=='zh-Hant' else '>>cmn_Hans<<')]+tokens
            limit=min(512,max(48,len(tokens)*3+16))
            def callback(step):
                nonlocal last_emit
                if cancel.is_set():return True
                if not step.is_last and step.token not in ('</s>','<s>','<pad>'):output.append(step.token)
                now=time.monotonic()
                if now-last_emit>=0.04:
                    partial=tgt.decode(output)
                    if partial:progress(previous+partial);last_emit=now
                return False
            try:
                result=model.translate_batch([source],beam_size=beam,max_input_length=0,max_decoding_length=limit,
                                             return_scores=False,callback=callback if beam==1 else None)[0]
            except RuntimeError as error:
                if self.backends[pair]['backend']!='cuda':raise
                self.details['gpu_fallback']=str(error)[:600];model=self.load(pair,cpu=True);output.clear();progress(previous)
                result=model.translate_batch([source],beam_size=beam,max_input_length=0,max_decoding_length=limit,
                                             return_scores=False,callback=callback if beam==1 else None)[0]
            if cancel.is_set():raise Cancelled()
            value=tgt.decode(result.hypotheses[0]).strip();limited=len(result.hypotheses[0])>=limit
            if target=='zh-Hant':value=traditional(value)
            if not value:raise ValueError('The model returned no translation.')
            truncated|=limited
            if not limited:self.remember(key,value)
            pieces.append(value+separator);progress(''.join(pieces).rstrip())
        value=''.join(pieces).strip()
        if not truncated:self.remember(cache_key,value)
        return dict(text=value,target=target,model='OPUS-MT INT8',truncated=truncated,cached=False,**self.details)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--threads',type=int)
    args=parser.parse_args();threads=args.threads or physical_cores()
    for key in ('OMP_NUM_THREADS','MKL_NUM_THREADS','OPENBLAS_NUM_THREADS'):os.environ[key]=str(threads)
    os.environ.setdefault('CT2_VERBOSE','1') # Official wheel dispatches supported AVX2/AVX-512 at runtime.
    if hasattr(sys.stdin,'reconfigure'):sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8',line_buffering=True)
    commands=queue.Queue(maxsize=4);active={};lock=threading.Lock()
    def emit(value):
        with lock:print(json.dumps(value,ensure_ascii=False,separators=(',',':')),flush=True)
    def read():
        try:
            for line in sys.stdin:
                if len(line)>100000:break
                value=json.loads(line);serial=value.get('id')
                if value.get('op')=='cancel':
                    if serial in active:active[serial].set()
                else:
                    event=threading.Event();active[serial]=event
                    try:commands.put_nowait((value,event))
                    except queue.Full:active.pop(serial,None);emit({'id':serial,'error':'Translation queue is full.'})
        finally:
            for event in list(active.values()):event.set()
            commands.put(None)
    threading.Thread(target=read,daemon=True).start()
    engine=Engine(args.root,max(1,threads));engine.warm();emit({'ready':engine.details})
    while True:
        item=commands.get()
        if item is None:break
        value,event=item;serial=value.get('id');start=time.monotonic()
        try:
            result=engine.translate(value['text'],value['target'],event,lambda text:emit({'id':serial,'partial':text}),value.get('mode','fast'))
            result['seconds']=round(time.monotonic()-start,4);emit({'id':serial,'result':result})
        except Cancelled:emit({'id':serial,'canceled':True})
        except Exception as error:emit({'id':serial,'error':str(error)[:2000]})
        finally:active.pop(serial,None)

if __name__=='__main__':main()
