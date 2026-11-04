using System;
using System.IO;
using System.Collections.Generic;
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
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr ConfigFn(IntPtr options,[MarshalAs(UnmanagedType.LPStr)]string key,[MarshalAs(UnmanagedType.LPStr)]string value);
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
        internal Session Open(string path){return new Session(this,path,1,1);}
        internal Session Open(string path,int inputCount,int outputCount){return new Session(this,path,inputCount,outputCount);}
        public void Dispose(){if(api!=IntPtr.Zero){if(memory!=IntPtr.Zero){Fn<ReleaseFn>(94)(memory);memory=IntPtr.Zero;}if(env!=IntPtr.Zero){Fn<ReleaseFn>(92)(env);env=IntPtr.Zero;}}if(module!=IntPtr.Zero){FreeLibrary(module);module=IntPtr.Zero;}}
        internal sealed class Tensor {internal float[] Data;internal long[] Shape;}
        internal sealed class InputData {
            internal Array Values;internal long[] Shape;internal int Type;
            internal static InputData Float(float[] values,long[] shape){return new InputData{Values=values,Shape=shape,Type=1};}
            internal static InputData Int64(long[] values,long[] shape){return new InputData{Values=values,Shape=shape,Type=7};}
            internal static InputData Bool(byte[] values,long[] shape){return new InputData{Values=values,Shape=shape,Type=9};}
        }
        internal sealed class Session : IDisposable {
            readonly OfflineRuntime runtime;readonly IntPtr[] inputNames,outputNames;
            IntPtr handle;
            internal Session(OfflineRuntime owner,string path,int inputCount,int outputCount) {
                runtime=owner;IntPtr options=IntPtr.Zero;
                try {
                    owner.Check(owner.Fn<OutFn>(10)(out options));owner.Check(owner.Fn<SettingFn>(23)(options,2));owner.Check(owner.Fn<SettingFn>(24)(options,Hardware.OcrThreads));
                    // Models run sequentially; idle encoder/detector pools must
                    // not compete with the active decoder or translation engine.
                    owner.Check(owner.Fn<SettingFn>(25)(options,1));
                    owner.Check(owner.Fn<ConfigFn>(130)(options,"session.intra_op.allow_spinning","0"));
                    owner.Check(owner.Fn<ConfigFn>(130)(options,"session.inter_op.allow_spinning","0"));
                    owner.Check(owner.Fn<SessionFn>(7)(owner.env,Path.GetFullPath(path),options,out handle));
                    inputNames=new IntPtr[inputCount];outputNames=new IntPtr[outputCount];
                    for(int i=0;i<inputNames.Length;i++)owner.Check(owner.Fn<NameFn>(36)(handle,new UIntPtr((uint)i),owner.allocator,out inputNames[i]));
                    for(int i=0;i<outputNames.Length;i++)owner.Check(owner.Fn<NameFn>(37)(handle,new UIntPtr((uint)i),owner.allocator,out outputNames[i]));
                }catch{Dispose();throw;}finally{if(options!=IntPtr.Zero)owner.Fn<ReleaseFn>(100)(options);}
            }
            internal Tensor Run(float[] values,long[] shape) {
                return Run(new[]{InputData.Float(values,shape)});
            }
            internal Tensor Run(InputData[] values) {
                if(values==null||values.Length!=inputNames.Length)throw new ArgumentException("The ONNX input count does not match the model.");
                IntPtr output=IntPtr.Zero,info=IntPtr.Zero;var inputsCreated=new IntPtr[values.Length];var pins=new List<GCHandle>();GCHandle names=default(GCHandle),inputs=default(GCHandle),outputs=default(GCHandle),results=default(GCHandle);
                var outputValues=new IntPtr[1];
                try {
                    for(int i=0;i<values.Length;i++){InputData value=values[i];GCHandle pin=GCHandle.Alloc(value.Values,GCHandleType.Pinned);pins.Add(pin);UIntPtr bytes=new UIntPtr((ulong)(value.Values.Length*System.Runtime.InteropServices.Marshal.SizeOf(value.Values.GetType().GetElementType())));runtime.Check(runtime.Fn<TensorFn>(49)(runtime.memory,pin.AddrOfPinnedObject(),bytes,value.Shape,new UIntPtr((uint)value.Shape.Length),value.Type,out inputsCreated[i]));}
                    names=GCHandle.Alloc(inputNames,GCHandleType.Pinned);inputs=GCHandle.Alloc(inputsCreated,GCHandleType.Pinned);outputs=GCHandle.Alloc(outputNames,GCHandleType.Pinned);results=GCHandle.Alloc(outputValues,GCHandleType.Pinned);
                    runtime.Check(runtime.Fn<RunFn>(9)(handle,IntPtr.Zero,names.AddrOfPinnedObject(),inputs.AddrOfPinnedObject(),new UIntPtr((uint)inputsCreated.Length),outputs.AddrOfPinnedObject(),new UIntPtr(1),results.AddrOfPinnedObject()));output=outputValues[0];
                    runtime.Check(runtime.Fn<InfoFn>(65)(output,out info));UIntPtr rank,count;runtime.Check(runtime.Fn<CountFn>(61)(info,out rank));runtime.Check(runtime.Fn<CountFn>(64)(info,out count));
                    if(rank.ToUInt64()>8||count.ToUInt64()>64000000)throw new IOException("Offline model output exceeds the limit.");
                    var dimensions=new long[(int)rank.ToUInt64()];runtime.Check(runtime.Fn<ShapeFn>(62)(info,dimensions,rank));IntPtr pointer;runtime.Check(runtime.Fn<InfoFn>(51)(output,out pointer));
                    var result=new float[(int)count.ToUInt64()];Marshal.Copy(pointer,result,0,result.Length);return new Tensor{Data=result,Shape=dimensions};
                }finally {
                    if(info!=IntPtr.Zero)runtime.Fn<ReleaseFn>(99)(info);if(outputValues[0]!=IntPtr.Zero)runtime.Fn<ReleaseFn>(96)(outputValues[0]);for(int i=0;i<inputsCreated.Length;i++)if(inputsCreated[i]!=IntPtr.Zero)runtime.Fn<ReleaseFn>(96)(inputsCreated[i]);foreach(GCHandle pin in pins)if(pin.IsAllocated)pin.Free();
                    if(results.IsAllocated)results.Free();if(outputs.IsAllocated)outputs.Free();if(inputs.IsAllocated)inputs.Free();if(names.IsAllocated)names.Free();
                }
            }
            public void Dispose(){if(inputNames!=null)foreach(IntPtr name in inputNames)if(name!=IntPtr.Zero)runtime.Check(runtime.Fn<FreeFn>(76)(runtime.allocator,name));if(outputNames!=null)foreach(IntPtr name in outputNames)if(name!=IntPtr.Zero)runtime.Check(runtime.Fn<FreeFn>(76)(runtime.allocator,name));if(handle!=IntPtr.Zero){runtime.Fn<ReleaseFn>(95)(handle);handle=IntPtr.Zero;}}
        }
    }
}
