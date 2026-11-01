using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MiniAI {
    internal sealed class Translation : IDisposable {
        internal static readonly Dictionary<string,string> Languages=new Dictionary<string,string>{{"zh","Chinese"},{"en","English"},{"zh-Hant","Traditional Chinese"}};
        readonly string root;
        readonly object gate=new object(),workerGate=new object(),writeGate=new object();
        readonly ConcurrentQueue<string> output=new ConcurrentQueue<string>();
        readonly AutoResetEvent arrived=new AutoResetEvent(false);
        Process worker;
        ProcessGroup group;
        volatile bool disposed;
        Dictionary<string,object> details;
        string lastError="",isa="auto";
        int generation;
        DateTime lastUse=DateTime.UtcNow;
        internal Translation(string directory){root=directory;}
        internal bool Available {get{return File.Exists(Path.Combine(root,"runtime/translation-ct2/python.exe"))&&File.Exists(Path.Combine(root,"runtime/translation-ct2/ct2_worker.py"))&&File.Exists(Path.Combine(root,"models/translation/eng-zho/model.bin"))&&File.Exists(Path.Combine(root,"models/translation/zho-eng/model.bin"));}}
        internal object Status {get{lock(workerGate){return details==null?(object)new{engine="CTranslate2",warmed=false,threads=Hardware.PhysicalCores,physical_cores=Hardware.PhysicalCores,beam_size=1,cpu_isa=isa,backend="starting",languages=new[]{"zh","en","zh-Hant"}}:new Dictionary<string,object>(details);}}}
        internal void Prewarm(){if(!Available)return;ThreadPool.QueueUserWorkItem(delegate{if(!Monitor.TryEnter(gate))return;try{Start(delegate{return disposed;});}catch(Exception error){lock(workerGate){lastError=error.Message;}}finally{Monitor.Exit(gate);}});}
        static string Quote(string value){return "\""+value.TrimEnd('\\').Replace("\"","")+"\"";}
        void Start(Func<bool> canceled) {
            if(disposed)throw new ObjectDisposedException("MiniAI translation");
            lock(workerGate) {
                if(worker==null||worker.HasExited) {
                    Guard.Check(Available,"Install the complete CTranslate2 offline pack.");
                    if(worker!=null){worker.Dispose();worker=null;}if(group!=null){group.Dispose();group=null;}
                    string stale;while(output.TryDequeue(out stale)){}details=null;lastError="";
                    var info=new ProcessStartInfo(Path.Combine(root,"runtime/translation-ct2/python.exe"),"-I -u -B "+Quote(Path.Combine(root,"runtime/translation-ct2/ct2_worker.py"))+" --root "+Quote(root)+" --threads "+Hardware.PhysicalCores){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.Combine(root,"runtime/translation-ct2")};
                    info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";info.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"]="1";
                    foreach(string name in new[]{"OMP_NUM_THREADS","MKL_NUM_THREADS","OPENBLAS_NUM_THREADS"})info.EnvironmentVariables[name]=Hardware.PhysicalCores.ToString();
                    worker=new Process{StartInfo=info};worker.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null){output.Enqueue(e.Data);arrived.Set();}};
                    worker.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;lock(workerGate){lastError+=e.Data+"\n";if(lastError.Length>6000)lastError=lastError.Substring(lastError.Length-6000);if(e.Data.IndexOf("Selected ISA",StringComparison.OrdinalIgnoreCase)>=0){isa=e.Data.Substring(e.Data.LastIndexOf(':')+1).Trim();if(details!=null)details["cpu_isa"]=isa;}}};
                    try{worker.Start();group=new ProcessGroup(worker);worker.BeginOutputReadLine();worker.BeginErrorReadLine();}catch{Stop();throw;}
                }
                if(details!=null)return;
            }
            var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<60000){if(canceled())throw new OperationCanceledException();var value=Next(100);if(value==null)continue;object ready;if(value.TryGetValue("ready",out ready)){lock(workerGate){details=(Dictionary<string,object>)ready;details["cpu_isa"]=isa;}lastUse=DateTime.UtcNow;return;}}
            Stop();throw new IOException("CTranslate2 warm-up exceeded the deadline. "+lastError);
        }
        Dictionary<string,object> Next(int milliseconds) {
            string line;if(output.TryDequeue(out line))return Json.Read(line);arrived.WaitOne(milliseconds);
            if(output.TryDequeue(out line))return Json.Read(line);
            lock(workerGate){if(worker==null||worker.HasExited)throw new IOException("CTranslate2 stopped. "+lastError);}return null;
        }
        void Send(object value){lock(writeGate){lock(workerGate){if(worker==null||worker.HasExited)throw new IOException("CTranslate2 is not running.");byte[] bytes=Encoding.UTF8.GetBytes(Json.Text(value)+"\n");worker.StandardInput.BaseStream.Write(bytes,0,bytes.Length);worker.StandardInput.BaseStream.Flush();}}}
        internal object Translate(string text,string target,Func<bool> canceled,Action<string> progress=null,string mode="fast") {
            while(!Monitor.TryEnter(gate,50)){if(canceled())throw new OperationCanceledException();}
            int serial=0;
            try {
                if(canceled())throw new OperationCanceledException();Start(canceled);lastUse=DateTime.UtcNow;serial=Interlocked.Increment(ref generation);Send(new{op="translate",id=serial,text=text,target=target,mode=mode});var watch=Stopwatch.StartNew();
                while(watch.ElapsedMilliseconds<300000) {
                    if(canceled())throw new OperationCanceledException();var value=Next(40);if(value==null||Json.Get(value,"id")!=serial.ToString())continue;
                    object item;if(value.TryGetValue("partial",out item)){if(progress!=null)progress(Convert.ToString(item));continue;}
                    if(value.TryGetValue("result",out item)){var result=(Dictionary<string,object>)item;result["cpu_isa"]=isa;lock(workerGate){details=new Dictionary<string,object>(result);details.Remove("text");details.Remove("target");details.Remove("seconds");details.Remove("cached");}lastUse=DateTime.UtcNow;return result;}
                    if(value.ContainsKey("canceled"))throw new OperationCanceledException();
                    if(value.ContainsKey("error"))throw new IOException(Json.Get(value,"error"));
                }
                throw new IOException("Translation exceeded the deadline; select a shorter passage.");
            }catch {if(serial!=0)try{Send(new{op="cancel",id=serial});}catch(IOException){}throw;}
            finally{lastUse=DateTime.UtcNow;Monitor.Exit(gate);}
        }
        void Stop(){lock(workerGate){details=null;if(group!=null){group.Dispose();group=null;}if(worker!=null){try{if(!worker.HasExited)worker.Kill();worker.WaitForExit(1000);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}worker.Dispose();worker=null;}}}
        internal void ReleaseIfIdle(){if(!Monitor.TryEnter(gate))return;try{if(worker!=null&&(DateTime.UtcNow-lastUse).TotalMinutes>=10)Stop();}finally{Monitor.Exit(gate);}}
        public void Dispose(){disposed=true;Stop();/* Event remains valid for callbacks already queued by Process. */}
    }
}
