"""Adapt the pinned official Hy-MT2 GGUF metadata to the pinned STQ runtime.

The upstream model predates the enum reservation in llama.cpp: tensor type 40
was STQ1_0 in its exporter, but is NVFP4 in the runtime. The quantized tensor
payload is left byte-for-byte unchanged. Unknown models are never patched.
"""
import argparse,hashlib,json,math,struct
from pathlib import Path

ORIGINAL='13a33fc4f72d5c92c439a65fd343696de4ccd0485bca84de2712bc0d8cc4e773'
RUNTIME='c828a2e883defef95fd6121ee1586efcce89ad40'

def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        while True:
            data=stream.read(1024*1024)
            if not data:break
            h.update(data)
    return h.hexdigest()

def prepare(path):
    path=Path(path);record=path.with_name('model-provenance.json');before=digest(path)
    if record.exists():
        old=json.loads(record.read_text(encoding='utf-8'))
        if before==old.get('prepared_sha256') and old.get('upstream_sha256')==ORIGINAL:
            print('Already prepared:',before);return old
    if before!=ORIGINAL:raise ValueError('This is not the pinned, unmodified official Hy-MT2 v2 model.')
    changes=[]
    with path.open('r+b') as stream:
        def number(fmt):return struct.unpack('<'+fmt,stream.read(struct.calcsize('<'+fmt)))[0]
        def string():return stream.read(number('Q')).decode('utf-8')
        def value(kind):
            if kind==8:return string()
            if kind==9:
                item=number('I');count=number('Q')
                if count>1000000:raise ValueError('Metadata array exceeds limit')
                return [value(item) for _ in range(count)]
            return number({0:'B',1:'b',2:'H',3:'h',4:'I',5:'i',6:'f',7:'?',10:'Q',11:'q',12:'d'}[kind])
        if stream.read(4)!=b'GGUF' or number('I')!=3:raise ValueError('Expected GGUF version 3')
        tensors=number('Q');entries=number('Q');metadata={};file_type=None
        for _ in range(entries):
            name=string();kind=number('I');position=stream.tell();metadata[name]=value(kind)
            if name=='general.file_type':file_type=position
        if metadata.get('general.architecture')!='hunyuan-dense' or tensors!=354:raise ValueError('Unexpected model architecture')
        quantized=[]
        for _ in range(tensors):
            name=string();dimensions=[number('Q') for _ in range(number('I'))];position=stream.tell();kind=number('I');offset=number('Q')
            if kind not in (0,14,40):raise ValueError('Unexpected tensor type '+str(kind))
            if kind==40:changes.append((position,43));quantized.append(name)
        if len(quantized)!=224 or file_type is None:raise ValueError('Unexpected STQ tensor layout')
        changes.append((file_type,42))
        for position,kind in changes:stream.seek(position);stream.write(struct.pack('<I',kind))
    result={'upstream_repo':'AngelSlim/Hy-MT2-1.8B-1.25Bit-GGUF','upstream_revision':'0989912c0cc2d3edeeecd76171d1c7d94ee17255','upstream_file':'Hy-MT2-1.8B-1.25bit-v2.gguf','upstream_sha256':ORIGINAL,'prepared_sha256':digest(path),'runtime_revision':RUNTIME,'metadata_tensor_type_remap':{'40':43},'metadata_file_type_remap':{'39':42},'tensor_descriptors_updated':len(quantized),'weights_changed':False}
    record.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2));return result

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('model');prepare(parser.parse_args().model)

