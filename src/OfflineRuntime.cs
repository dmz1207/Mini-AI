using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniAI {
    // A small binding to the stable ONNX Runtime C API; no Python or managed ML runtime is shipped.
    internal sealed class OfflineRuntime : IDisposable {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)]static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr ApiBaseFn();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr GetApiFn(uint version);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr EnvFn(int level,[MarshalAs(UnmanagedType.LPStr)]string id,out IntPtr env);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr OutFn(out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr MemoryFn(int allocator,int memory,out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr SessionFn(IntPtr env,[MarshalAs(UnmanagedType.LPWStr)]string path,IntPtr options,out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr SettingFn(IntPtr options,int value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr PointerFn(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate void ReleaseFn(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr FreeFn(IntPtr allocator,IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr NameFn(IntPtr session,UIntPtr index,IntPtr allocator,out IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr TensorFn(IntPtr memory,IntPtr data,UIntPtr bytes,[In]long[] shape,UIntPtr rank,int type,out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr RunFn(IntPtr session,IntPtr options,IntPtr names,IntPtr values,UIntPtr count,IntPtr outputs,UIntPtr outputCount,IntPtr results);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr InfoFn(IntPtr value,out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr CountFn(IntPtr value,out UIntPtr count);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr ShapeFn(IntPtr value,[Out]long[] shape,UIntPtr count);
        IntPtr module,api,env,memory,allocator;
        internal OfflineRuntime(string path) {
            try {
                NativeDependencies.Check(path);
                module=LoadLibraryEx(Path.GetFullPath(path),IntPtr.Zero,0x900);if(module==IntPtr.Zero)throw new IOException("Cannot load the offline OCR runtime (Windows x64 and the VC++ runtime are required).");
                var getBase=(ApiBaseFn)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module,"OrtGetApiBase"),typeof(ApiBaseFn));
                var getApi=(GetApiFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(getBase()),typeof(GetApiFn));api=getApi(17);
                if(api==IntPtr.Zero)throw new IOException("The offline runtime does not support API 17.");
                Check(Fn<EnvFn>(3)(3,"MiniPDF offline OCR",out env));Check(Fn<PointerFn>(6)(env));
                Check(Fn<MemoryFn>(69)(1,0,out memory));Check(Fn<OutFn>(78)(out allocator));
            }catch{Dispose();throw;}
        }
        T Fn<T>(int index) where T:class {return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(api,index*IntPtr.Size),typeof(T)) as T;}
        void Check(IntPtr status){if(status==IntPtr.Zero)return;string error=Utf8(Fn<PointerFn>(2)(status));Fn<ReleaseFn>(93)(status);throw new IOException(error);}
        static string Utf8(IntPtr pointer){int length=0;while(length<65536&&Marshal.ReadByte(pointer,length)!=0)length++;var bytes=new byte[length];Marshal.Copy(pointer,bytes,0,length);return Encoding.UTF8.GetString(bytes);}
        internal Session Open(string path){return new Session(this,path);}
        public void Dispose(){if(api!=IntPtr.Zero){if(memory!=IntPtr.Zero){Fn<ReleaseFn>(94)(memory);memory=IntPtr.Zero;}if(env!=IntPtr.Zero){Fn<ReleaseFn>(92)(env);env=IntPtr.Zero;}}if(module!=IntPtr.Zero){FreeLibrary(module);module=IntPtr.Zero;}}
        internal sealed class Tensor {internal float[] Data;internal long[] Shape;}
        internal sealed class Session : IDisposable {
            readonly OfflineRuntime runtime;
            IntPtr handle,inputName,outputName;
            internal Session(OfflineRuntime owner,string path) {
                runtime=owner;IntPtr options=IntPtr.Zero;
                try {
                    owner.Check(owner.Fn<OutFn>(10)(out options));owner.Check(owner.Fn<SettingFn>(23)(options,2));owner.Check(owner.Fn<SettingFn>(24)(options,Math.Max(1,Math.Min(4,Environment.ProcessorCount))));
                    owner.Check(owner.Fn<SessionFn>(7)(owner.env,Path.GetFullPath(path),options,out handle));
                    owner.Check(owner.Fn<NameFn>(36)(handle,UIntPtr.Zero,owner.allocator,out inputName));owner.Check(owner.Fn<NameFn>(37)(handle,UIntPtr.Zero,owner.allocator,out outputName));
                }catch{Dispose();throw;}finally{if(options!=IntPtr.Zero)owner.Fn<ReleaseFn>(100)(options);}
            }
            internal Tensor Run(float[] values,long[] shape) {
                IntPtr input=IntPtr.Zero,output=IntPtr.Zero,info=IntPtr.Zero;
                var data=GCHandle.Alloc(values,GCHandleType.Pinned);GCHandle names=default(GCHandle),inputs=default(GCHandle),outputs=default(GCHandle),results=default(GCHandle);
                var outputValues=new IntPtr[1];
                try {
                    runtime.Check(runtime.Fn<TensorFn>(49)(runtime.memory,data.AddrOfPinnedObject(),new UIntPtr((ulong)values.LongLength*4),shape,new UIntPtr((uint)shape.Length),1,out input));
                    names=GCHandle.Alloc(new[]{inputName},GCHandleType.Pinned);inputs=GCHandle.Alloc(new[]{input},GCHandleType.Pinned);outputs=GCHandle.Alloc(new[]{outputName},GCHandleType.Pinned);results=GCHandle.Alloc(outputValues,GCHandleType.Pinned);
                    runtime.Check(runtime.Fn<RunFn>(9)(handle,IntPtr.Zero,names.AddrOfPinnedObject(),inputs.AddrOfPinnedObject(),new UIntPtr(1),outputs.AddrOfPinnedObject(),new UIntPtr(1),results.AddrOfPinnedObject()));output=outputValues[0];
                    runtime.Check(runtime.Fn<InfoFn>(65)(output,out info));UIntPtr rank,count;runtime.Check(runtime.Fn<CountFn>(61)(info,out rank));runtime.Check(runtime.Fn<CountFn>(64)(info,out count));
                    if(rank.ToUInt64()>8||count.ToUInt64()>64000000)throw new IOException("Offline model output exceeds the limit.");
                    var dimensions=new long[(int)rank.ToUInt64()];runtime.Check(runtime.Fn<ShapeFn>(62)(info,dimensions,rank));IntPtr pointer;runtime.Check(runtime.Fn<InfoFn>(51)(output,out pointer));
                    var result=new float[(int)count.ToUInt64()];Marshal.Copy(pointer,result,0,result.Length);return new Tensor{Data=result,Shape=dimensions};
                }finally {
                    if(info!=IntPtr.Zero)runtime.Fn<ReleaseFn>(99)(info);if(outputValues[0]!=IntPtr.Zero)runtime.Fn<ReleaseFn>(96)(outputValues[0]);if(input!=IntPtr.Zero)runtime.Fn<ReleaseFn>(96)(input);
                    if(results.IsAllocated)results.Free();if(outputs.IsAllocated)outputs.Free();if(inputs.IsAllocated)inputs.Free();if(names.IsAllocated)names.Free();data.Free();
                }
            }
            public void Dispose(){if(inputName!=IntPtr.Zero){runtime.Check(runtime.Fn<FreeFn>(76)(runtime.allocator,inputName));inputName=IntPtr.Zero;}if(outputName!=IntPtr.Zero){runtime.Check(runtime.Fn<FreeFn>(76)(runtime.allocator,outputName));outputName=IntPtr.Zero;}if(handle!=IntPtr.Zero){runtime.Fn<ReleaseFn>(95)(handle);handle=IntPtr.Zero;}}
        }
    }
}
