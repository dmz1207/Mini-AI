using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiniAI
{
    // Close owned inference workers when MiniAI exits.
    internal sealed class ProcessGroup : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Basic
        {
            internal long ProcessTime,JobTime;
            internal uint Flags;
            internal UIntPtr MinimumWorkingSet,MaximumWorkingSet;
            internal uint ActiveProcesses;
            internal UIntPtr Affinity;
            internal uint Priority,Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct Io
        {
            internal ulong Read,Write,Other,ReadBytes,WriteBytes,OtherBytes;
        }
        [StructLayout(LayoutKind.Sequential)] struct Limits
        {
            internal Basic Basic;
            internal Io Io;
            internal UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool SetInformationJobObject(SafeFileHandle job,int kind,ref Limits limits,uint size);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
        SafeFileHandle handle;
        internal ProcessGroup(Process process)
        {
            handle=CreateJobObject(IntPtr.Zero,null);
            var limits=new Limits();
            limits.Basic.Flags=0x2000; // Kill on close.
            if(handle.IsInvalid || !SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits))) ||
                !AssignProcessToJobObject(handle,process.Handle))
            {
                int error=Marshal.GetLastWin32Error();
                handle.Dispose(); handle=null;
                throw new System.ComponentModel.Win32Exception(error,"Cannot manage the MiniAI inference process.");
            }
        }
        public void Dispose() { if(handle!=null) { handle.Dispose(); handle=null; } }
    }
}
