using System;
using System.Runtime.InteropServices;

namespace MiniAI {
    internal static class Hardware {
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetLogicalProcessorInformationEx(int relationship,IntPtr buffer,ref uint size);
        internal static readonly int PhysicalCores=CountCores();
        static int CountCores() {
            uint size=0;GetLogicalProcessorInformationEx(0,IntPtr.Zero,ref size);if(size==0||size>1024*1024)return Math.Max(1,Environment.ProcessorCount);
            IntPtr buffer=Marshal.AllocHGlobal((int)size);
            try {if(!GetLogicalProcessorInformationEx(0,buffer,ref size))return Math.Max(1,Environment.ProcessorCount);int cores=0;long offset=0;while(offset+8<=size){IntPtr row=new IntPtr(buffer.ToInt64()+offset);int length=Marshal.ReadInt32(row,4);if(length<8||offset+length>size)break;if(Marshal.ReadInt32(row)==0)cores++;offset+=length;}return Math.Max(1,cores);}
            finally{Marshal.FreeHGlobal(buffer);}
        }
    }
}
