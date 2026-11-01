"""Check PE import closure without trusting PATH or developer-installed DLLs."""
import argparse,json,os,struct
from pathlib import Path

SYSTEM=set('kernel32.dll kernelbase.dll ntdll.dll advapi32.dll bcrypt.dll crypt32.dll cryptbase.dll cryptsp.dll sechost.dll rpcrt4.dll combase.dll ole32.dll oleaut32.dll shell32.dll shlwapi.dll ws2_32.dll mswsock.dll iphlpapi.dll user32.dll gdi32.dll gdi32full.dll comdlg32.dll comctl32.dll version.dll winmm.dll powrprof.dll setupapi.dll cfgmgr32.dll psapi.dll secur32.dll sspicli.dll userenv.dll uxtheme.dll dwmapi.dll normaliz.dll ncrypt.dll wintrust.dll winhttp.dll wininet.dll dbghelp.dll imm32.dll wtsapi32.dll msvcrt.dll ucrtbase.dll mscoree.dll'.split())

def imports(path):
    data=Path(path).read_bytes()
    if data[:2]!=b'MZ':raise ValueError('Not a PE file: '+str(path))
    pe=struct.unpack_from('<I',data,60)[0]
    if data[pe:pe+4]!=b'PE\0\0':raise ValueError('Invalid PE signature')
    count=struct.unpack_from('<H',data,pe+6)[0];opt=pe+24
    magic=struct.unpack_from('<H',data,opt)[0]
    if magic not in (0x10b,0x20b):raise ValueError('Invalid optional header')
    sections=opt+struct.unpack_from('<H',data,pe+20)[0]
    segments=[struct.unpack_from('<IIII',data,sections+40*i+8) for i in range(count)]
    def offset(rva):
        for size,va,raw_size,raw in segments:
            if va<=rva<va+max(size,raw_size):return rva-va+raw
        if 0<=rva<struct.unpack_from('<I',data,opt+60)[0]:return rva
        raise ValueError('Invalid PE RVA')
    directories=opt+(112 if magic==0x20b else 96)
    result=set()
    def name(rva):
        start=offset(rva);end=data.index(0,start)
        if end-start>260:raise ValueError('Invalid import name')
        return data[start:end].decode('ascii').lower()
    regular=struct.unpack_from('<II',data,directories+8)
    if regular[0]:
        pos=offset(regular[0]);limit=pos+regular[1]
        while pos+20<=limit and any(data[pos:pos+20]):
            result.add(name(struct.unpack_from('<I',data,pos+12)[0]));pos+=20
    delayed=struct.unpack_from('<II',data,directories+13*8)
    if delayed[0]:
        pos=offset(delayed[0]);limit=pos+delayed[1]
        base=struct.unpack_from('<Q' if magic==0x20b else '<I',data,opt+(24 if magic==0x20b else 28))[0]
        while pos+32<=limit and any(data[pos:pos+32]):
            flags,rva=struct.unpack_from('<II',data,pos)
            result.add(name(rva if flags&1 else rva-base));pos+=32
    return sorted(result)

def system(name):return name in SYSTEM or name.startswith(('api-ms-win-','ext-ms-win-'))

def check(paths,system_only=False):
    records=[];missing=[];visited=set()
    def visit(path):
        path=path.resolve()
        if path in visited:return
        visited.add(path);names=imports(path);records.append({'file':str(path),'imports':names})
        siblings={p.name.lower():p for p in path.parent.iterdir() if p.is_file()}
        for name in names:
            if system(name):continue
            if not system_only and name in siblings:visit(siblings[name])
            else:missing.append({'file':str(path),'dependency':name})
    for path in paths:visit(Path(path))
    if missing:raise ValueError('Unbundled native dependencies: '+json.dumps(missing))
    return records

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path);parser.add_argument('--file',type=Path);parser.add_argument('--system-only',action='store_true');parser.add_argument('--output',type=Path);args=parser.parse_args()
    paths=[args.file] if args.file else [p for p in args.root.rglob('*') if p.suffix.lower() in ('.exe','.dll') and 'work' not in p.relative_to(args.root).parts and 'state' not in p.relative_to(args.root).parts]
    records=check(paths,args.system_only)
    if args.output:args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'checked':len(records),'missing':0}))
