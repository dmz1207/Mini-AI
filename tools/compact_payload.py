"""Losslessly compact the EXE payload with Windows' built-in LZMS codec.

The ordinary ZIP remains standard. Only the private EXE container uses framed
LZMS blocks and a reversible four-byte shuffle for floating-point ONNX data.
"""
import argparse,concurrent.futures,ctypes,json,os,shutil,struct,zipfile
from pathlib import Path

cab=ctypes.WinDLL('cabinet',use_last_error=True);size_t=ctypes.c_size_t
cab.CreateCompressor.argtypes=[ctypes.c_uint32,ctypes.c_void_p,ctypes.POINTER(ctypes.c_void_p)];cab.Compress.argtypes=[ctypes.c_void_p,ctypes.c_void_p,size_t,ctypes.c_void_p,size_t,ctypes.POINTER(size_t)];cab.CloseCompressor.argtypes=[ctypes.c_void_p]
cab.CreateDecompressor.argtypes=cab.CreateCompressor.argtypes;cab.Decompress.argtypes=cab.Compress.argtypes;cab.CloseDecompressor.argtypes=[ctypes.c_void_p]
def transform(data,encode,expected=0):
    handle=ctypes.c_void_p();create=cab.CreateCompressor if encode else cab.CreateDecompressor;close=cab.CloseCompressor if encode else cab.CloseDecompressor;call=cab.Compress if encode else cab.Decompress
    if not create(5,None,ctypes.byref(handle)):raise ctypes.WinError(ctypes.get_last_error())
    try:
        source=ctypes.create_string_buffer(data);count=size_t(expected)
        if not expected:call(handle,source,len(data),None,0,ctypes.byref(count))
        if not count.value or count.value>16*1024*1024:raise ValueError('Invalid compressed block size')
        output=ctypes.create_string_buffer(count.value)
        if not call(handle,source,len(data),output,len(output),ctypes.byref(count)):raise ctypes.WinError(ctypes.get_last_error())
        return output.raw[:count.value]
    finally:close(handle)
def shuffle(data):
    end=len(data)//4*4;return b''.join(data[i:end:4] for i in range(4))+data[end:]
def encode_block(data,shuffled):
    source=shuffle(data) if shuffled else data;packed=transform(source,True)
    if transform(packed,False,len(source))!=source:raise ValueError('LZMS round-trip failed')
    return struct.pack('<II',len(data),len(packed))+packed
def compact(package,output):
    package=Path(package);output=Path(output);codecs={};stats=[]
    with zipfile.ZipFile(package) as source,zipfile.ZipFile(output,'w',allowZip64=False) as target,concurrent.futures.ThreadPoolExecutor(max_workers=min(4,os.cpu_count() or 1)) as pool:
        for entry in source.infolist():
            selected=entry.file_size>=1024*1024 and entry.filename.lower().endswith(('.onnx','.exe','.dll'))
            if selected:
                shuffled=entry.filename.lower().endswith('.onnx');codecs[entry.filename[len('MiniAI/'):]]='lzms-shuffle4' if shuffled else 'lzms';packed_bytes=20
                with source.open(entry) as raw,target.open(entry.filename,'w') as dest:
                    dest.write(b'MAIPK01\0'+struct.pack('<IQ',1 if shuffled else 0,entry.file_size))
                    while True:
                        blocks=[raw.read(8*1024*1024) for _ in range(4)];blocks=[b for b in blocks if b]
                        if not blocks:break
                        for block in pool.map(encode_block,blocks,[shuffled]*len(blocks)):dest.write(block);packed_bytes+=len(block)
                stats.append({'file':entry.filename,'deflate_bytes':entry.compress_size,'compact_bytes':packed_bytes});print(json.dumps(stats[-1]),flush=True)
            else:
                info=zipfile.ZipInfo(entry.filename,entry.date_time);info.compress_type=entry.compress_type
                with source.open(entry) as raw,target.open(info,'w') as dest:shutil.copyfileobj(raw,dest,1024*1024)
        target.writestr('MiniAI/portable-codecs.json',json.dumps({'format':1,'files':codecs},separators=(',',':')))
    record={'standard_zip_bytes':package.stat().st_size,'compact_zip_bytes':output.stat().st_size,'saved_bytes':package.stat().st_size-output.stat().st_size,'files':stats}
    output.with_suffix('.json').write_text(json.dumps(record,indent=2),encoding='utf-8');print(json.dumps({k:v for k,v in record.items() if k!='files'}),flush=True)
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--package',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();compact(a.package,a.output)
