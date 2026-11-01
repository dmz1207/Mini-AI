"""Reuse ZIP-compressed bytes and adjust offsets for a streaming, single-EXE package."""
import argparse,json,shutil,struct,zipfile
from pathlib import Path
from pack import sha256

def build(package,stub,output):
    package=Path(package);stub=Path(stub);output=Path(output);output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(package) as source:
        if source.start_dir>=0xffffffff:raise ValueError('ZIP64 is not supported by this small wrapper')
        with output.open('wb') as stream:
            stream.write(stub.read_bytes())
            with package.open('rb') as payload:shutil.copyfileobj(payload,stream,1024*1024)
    delta=stub.stat().st_size
    with output.open('r+b') as stream:
        stream.seek(-min(output.stat().st_size,65557),2);tail=stream.read();pos=tail.rfind(b'PK\x05\x06')
        if pos<0:raise ValueError('Missing ZIP directory')
        eocd=output.stat().st_size-len(tail)+pos
        count=struct.unpack_from('<H',tail,pos+10)[0];directory=struct.unpack_from('<I',tail,pos+16)[0]
        stream.seek(eocd+16);stream.write(struct.pack('<I',directory+delta))
        cursor=directory+delta
        for _ in range(count):
            stream.seek(cursor);entry=stream.read(46)
            if entry[:4]!=b'PK\x01\x02':raise ValueError('Invalid ZIP directory')
            offset=struct.unpack_from('<I',entry,42)[0];stream.seek(cursor+42);stream.write(struct.pack('<I',offset+delta))
            cursor+=46+sum(struct.unpack_from('<HHH',entry,28))
    with zipfile.ZipFile(output) as final:
        manifest=json.loads(final.read('MiniAI/package-checksums.json'))
        if final.testzip() is not None:raise ValueError('Single EXE payload CRC failed')
        extra=1 if 'MiniAI/portable-codecs.json' in final.namelist() else 0
        if len(final.namelist())!=len(manifest)+1+extra:raise ValueError('Unexpected package contents')
    record={'bytes':output.stat().st_size,'sha256':sha256(output),'files':len(manifest)}
    output.with_suffix('.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8');print(json.dumps(record))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--package',type=Path,required=True);parser.add_argument('--manifest',type=Path);parser.add_argument('--stub',type=Path);parser.add_argument('--output',type=Path);args=parser.parse_args()
    if args.manifest:
        with zipfile.ZipFile(args.package) as source:args.manifest.write_bytes(source.read('MiniAI/package-checksums.json'))
    else:build(args.package,args.stub,args.output)
