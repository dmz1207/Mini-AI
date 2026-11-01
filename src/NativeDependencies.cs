using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MiniAI {
    // Check imports before Windows' loader can display missing-DLL dialogs.
    internal static class NativeDependencies {
        static readonly HashSet<string> SystemNames=new HashSet<string>(("kernel32.dll kernelbase.dll ntdll.dll advapi32.dll bcrypt.dll crypt32.dll cryptbase.dll cryptsp.dll sechost.dll rpcrt4.dll combase.dll ole32.dll oleaut32.dll shell32.dll shlwapi.dll ws2_32.dll mswsock.dll iphlpapi.dll user32.dll gdi32.dll gdi32full.dll comdlg32.dll comctl32.dll version.dll winmm.dll powrprof.dll setupapi.dll cfgmgr32.dll psapi.dll secur32.dll sspicli.dll userenv.dll uxtheme.dll dwmapi.dll normaliz.dll ncrypt.dll wintrust.dll winhttp.dll wininet.dll dbghelp.dll imm32.dll wtsapi32.dll msvcrt.dll ucrtbase.dll mscoree.dll").Split(' '),StringComparer.OrdinalIgnoreCase);
        internal static void Check(string path){Visit(Path.GetFullPath(path),new HashSet<string>(StringComparer.OrdinalIgnoreCase));}
        static void Visit(string path,HashSet<string> seen) {
            if(!seen.Add(path))return;
            foreach(string name in Imports(path)) {
                if(SystemNames.Contains(name)||name.StartsWith("api-ms-win-",StringComparison.OrdinalIgnoreCase)||name.StartsWith("ext-ms-win-",StringComparison.OrdinalIgnoreCase))continue;
                if(name!=Path.GetFileName(name))throw new IOException("Invalid native dependency name.");
                string dependency=Path.Combine(Path.GetDirectoryName(path),name);
                if(!File.Exists(dependency))throw new IOException("Incomplete MiniAI package: missing "+name+" beside "+Path.GetFileName(path)+". Run the complete MiniAI-Portable.exe to repair it. / 功能包缺少组件，请运行完整单文件包修复。");
                Visit(dependency,seen);
            }
        }
        static IEnumerable<string> Imports(string path) {
            byte[] data=File.ReadAllBytes(path);Func<int,uint> u32=delegate(int p){return BitConverter.ToUInt32(data,p);};
            int pe=checked((int)u32(60)),opt=pe+24;bool x64=BitConverter.ToUInt16(data,opt)==0x20b;
            if(data[0]!='M'||data[1]!='Z'||u32(pe)!=0x4550)throw new IOException("Invalid native executable: "+path);
            int count=BitConverter.ToUInt16(data,pe+6),sections=opt+BitConverter.ToUInt16(data,pe+20);
            Func<uint,int> offset=delegate(uint rva){for(int i=0;i<count;i++){int p=sections+i*40;uint size=Math.Max(u32(p+8),u32(p+16)),va=u32(p+12);if(rva>=va&&(ulong)rva<(ulong)va+size)return checked((int)(rva-va+u32(p+20)));}if(rva<u32(opt+60))return checked((int)rva);throw new IOException("Invalid native import table.");};
            Func<uint,string> name=delegate(uint rva){int p=offset(rva),n=0;while(n<260&&data[p+n]!=0)n++;if(n==260)throw new IOException("Invalid native import name.");return Encoding.ASCII.GetString(data,p,n);};
            int directories=opt+(x64?112:96);uint start=u32(directories+8),length=u32(directories+12);
            if(start!=0){int pos=offset(start);for(int i=0;i<length/20;i++,pos+=20){uint rva=u32(pos+12);if(rva==0)break;yield return name(rva);}}
            start=u32(directories+13*8);length=u32(directories+13*8+4);
            if(start!=0){int pos=offset(start);ulong imageBase=x64?BitConverter.ToUInt64(data,opt+24):u32(opt+28);for(int i=0;i<length/32;i++,pos+=32){uint flags=u32(pos),rva=u32(pos+4);if(rva==0)break;yield return name((flags&1)!=0?rva:checked((uint)((ulong)rva-imageBase)));}}
        }
    }
}
