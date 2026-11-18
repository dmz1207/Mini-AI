using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MiniAI {
    // A persistent, owned process isolates Texo's ggml library from other
    // inference engines and lets cancellation stop native work immediately.
    internal sealed class TexoWorker : IDisposable {
        readonly string root;
        Process process;
        ProcessGroup group;
        NamedPipeServerStream pipe;
        internal string Cpu {get;private set;}
        internal TexoWorker(string folder){root=Path.GetFullPath(folder);}
        static Task Observe(Task pending){
            // A canceled request closes the pipe while its I/O callback is still
            // pending. FromAsync must finish EndRead/EndWrite; observe that fault
            // even when the caller has already stopped waiting.
            pending.ContinueWith(delegate(Task ended){var ignored=ended.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            return pending;
        }
        void Wait(Task pending,Func<bool> canceled,int seconds){
            var timer=Stopwatch.StartNew();
            try{while(!pending.Wait(30)){
                if(canceled!=null&&canceled())throw new OperationCanceledException();
                if(process==null||process.HasExited)throw new IOException("Texo worker stopped unexpectedly. / 公式识别进程已退出。");
                if(timer.Elapsed.TotalSeconds>seconds)throw new IOException("Texo recognition timed out. Try a smaller formula region. / 公式识别超时，请缩小区域。");
            }}catch(AggregateException){pending.GetAwaiter().GetResult();throw;}
            if(canceled!=null&&canceled())throw new OperationCanceledException();
        }
        void Start(Func<bool> canceled){
            if(process!=null&&!process.HasExited&&pipe!=null&&pipe.IsConnected)return;
            Dispose();string name="MiniAI-Texo-"+Guid.NewGuid().ToString("N");
            var security=new PipeSecurity();security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
            pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
            // Never close an APM AsyncWaitHandle manually: Windows can signal
            // the native event before PipeStream.AsyncPSCallback calls Set().
            // Closing it in that interval terminates the whole service.
            var channel=pipe;
            var connection=Observe(Task.Factory.FromAsync(channel.BeginWaitForConnection,channel.EndWaitForConnection,null));
            try{
                var info=new ProcessStartInfo(Path.Combine(root,"MiniAI.exe"),"--formula-worker "+name){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true};
                info.EnvironmentVariables["CRISPEMBED_OFFLINE"]="1";
                process=Process.Start(info);process.OutputDataReceived+=delegate{};process.ErrorDataReceived+=delegate{};process.BeginOutputReadLine();process.BeginErrorReadLine();
                group=new ProcessGroup(process);Wait(connection,canceled,15);
            }catch{Dispose();throw;}
        }
        byte[] Read(int count,Func<bool> canceled){
            var data=new byte[count];int offset=0;
            while(offset<count){var channel=pipe;int start=offset;var pending=Task<int>.Factory.FromAsync(delegate(AsyncCallback callback,object state){return channel.BeginRead(data,start,count-start,callback,state);},channel.EndRead,null);Wait(Observe(pending),canceled,60);int read=pending.Result;if(read==0)throw new EndOfStreamException("Texo worker disconnected.");offset+=read;}
            return data;
        }
        void Write(byte[] data,Func<bool> canceled){var channel=pipe;var pending=Observe(Task.Factory.FromAsync(delegate(AsyncCallback callback,object state){return channel.BeginWrite(data,0,data.Length,callback,state);},channel.EndWrite,null));Wait(pending,canceled,15);}
        internal string Recognize(byte[] gray,int width,int height,out float confidence,Func<bool> canceled){
            confidence=0;
            try{
                Start(canceled);var header=new byte[12];Buffer.BlockCopy(BitConverter.GetBytes(width),0,header,0,4);Buffer.BlockCopy(BitConverter.GetBytes(height),0,header,4,4);Buffer.BlockCopy(BitConverter.GetBytes(gray.Length),0,header,8,4);
                Write(header,canceled);Write(gray,canceled);var lengthBytes=Read(4,canceled);int length=BitConverter.ToInt32(lengthBytes,0);
                if(length<1||length>1024*1024)throw new IOException("Invalid Texo reply size.");
                var result=Json.Read(Encoding.UTF8.GetString(Read(length,canceled)));string error=Json.Get(result,"error");if(error.Length>0)throw new IOException(error);
                object score;if(result.TryGetValue("confidence",out score))confidence=Convert.ToSingle(score,System.Globalization.CultureInfo.InvariantCulture);
                Cpu=Json.Get(result,"cpu");
                return Json.Get(result,"tex");
            }catch{Dispose();throw;}
        }
        public void Dispose(){
            if(pipe!=null){pipe.Dispose();pipe=null;}if(group!=null){group.Dispose();group=null;}
            if(process!=null){try{if(!process.HasExited){process.Kill();process.WaitForExit(3000);}}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}finally{process.Dispose();process=null;}}
        }
        internal static int Run(string root,string name){
            Guid id;if(!name.StartsWith("MiniAI-Texo-",StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring("MiniAI-Texo-".Length),"N",out id))return 2;
            try{using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut)){
                pipe.Connect(15000);using(var input=new BinaryReader(pipe,Encoding.UTF8,true))using(var output=new BinaryWriter(pipe,Encoding.UTF8,true))using(var native=new TexoNative(root)){
                    while(true){int width;try{width=input.ReadInt32();}catch(EndOfStreamException){return 0;}
                        int height=input.ReadInt32(),length=input.ReadInt32();
                        if(width<1||height<1||width>20000||height>20000||length<1||length>24000000||(long)width*height!=length)return 2;
                        byte[] gray=input.ReadBytes(length);if(gray.Length!=length)return 2;
                        object reply;try{float confidence;string tex=native.Recognize(gray,width,height,out confidence);reply=new{tex=tex,confidence=confidence,cpu=native.Cpu,error=""};}catch(Exception error){reply=new{tex="",confidence=0,cpu=native.Cpu,error=error.Message};}
                        byte[] response=Encoding.UTF8.GetBytes(Json.Text(reply));output.Write(response.Length);output.Write(response);output.Flush();
                    }
                }
            }}catch(IOException){return 1;}catch(Exception){return 2;}
        }
    }
    internal sealed class TexoNative : IDisposable {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)]static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr InitFn(byte[] model,int threads);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr ReadFn(IntPtr context,byte[] image,int width,int height,int channels,out int length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate float ConfidenceFn(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr CountsFn(IntPtr context,out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void FreeFn(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate IntPtr CpuFn();
        internal string Cpu {get;private set;}
        IntPtr module,context;ReadFn read;ConfidenceFn mean;CountsFn counts;FreeFn free;
        internal TexoNative(string root){
            try{
                string dll=Path.Combine(root,"runtime/formula/crispembed.dll");NativeDependencies.Check(dll);
                module=LoadLibraryEx(Path.GetFullPath(dll),IntPtr.Zero,0x900);if(module==IntPtr.Zero)throw new IOException("Cannot load the Texo runtime. / 无法加载公式识别组件。");
                var init=Function<InitFn>("crispembed_ocr_model_init");read=Function<ReadFn>("crispembed_ocr_model_recognize");mean=Function<ConfidenceFn>("crispembed_ocr_model_mean_confidence");counts=Function<CountsFn>("crispembed_ocr_model_confidences");free=Function<FreeFn>("crispembed_ocr_model_free");
                Cpu=Marshal.PtrToStringAnsi(Function<CpuFn>("miniai_formula_cpu_features")());
                string model=Path.GetFullPath(Path.Combine(root,"models/formula/texo-distill-q8_0.gguf"));context=init(Encoding.UTF8.GetBytes(model+"\0"),Hardware.FormulaThreads);
                if(context==IntPtr.Zero)throw new IOException("Cannot initialize Texo Q8_0. / 无法初始化公式识别模型。");
            }catch{Dispose();throw;}
        }
        T Function<T>(string name) where T:class{IntPtr address=GetProcAddress(module,name);if(address==IntPtr.Zero)throw new IOException("Texo runtime is missing "+name);return Marshal.GetDelegateForFunctionPointer(address,typeof(T)) as T;}
        internal string Recognize(byte[] gray,int width,int height,out float confidence){
            int length;IntPtr text=read(context,gray,width,height,1,out length);
            if(text==IntPtr.Zero||length<0||length>100000)throw new IOException("Texo returned no formula. / 未识别出公式。");
            var bytes=new byte[length];Marshal.Copy(text,bytes,0,length);float score=mean(context);confidence=Single.IsNaN(score)||Single.IsInfinity(score)?0:Math.Max(0,Math.Min(1,score));
            int tokens;counts(context,out tokens);if(tokens>=512){confidence=0;return "";}
            return Encoding.UTF8.GetString(bytes).Trim();
        }
        public void Dispose(){if(context!=IntPtr.Zero){free(context);context=IntPtr.Zero;}if(module!=IntPtr.Zero){FreeLibrary(module);module=IntPtr.Zero;}}
    }
}
