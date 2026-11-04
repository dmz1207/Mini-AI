using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MiniAI {
    // C# owner for the pinned STQ-enabled llama.cpp server. Regular LLamaSharp
    // packages do not implement the STQ1_0 tensors used by this model.
    internal sealed class HyTranslation : IDisposable {
        const string Model="models/translation/hy-mt2.gguf";
        readonly string root;
        readonly object processGate=new object(),logGate=new object(),inferenceGate=new object();
        readonly Dictionary<string,Dictionary<string,object>> cache=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);
        readonly Queue<string> cacheOrder=new Queue<string>();
        readonly StringBuilder errors=new StringBuilder();
        Process server;
        ProcessGroup group;
        string url,key,isa="not loaded",device="CPU";
        DateTime lastUse=DateTime.UtcNow;
        volatile bool disposed,ready;
        bool gpuFailed;
        int pendingRequests;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool IsProcessorFeaturePresent(uint feature);

        internal HyTranslation(string directory){root=Path.GetFullPath(directory);}
        internal bool Available {
            get {
                if(!File.Exists(Path.Combine(root,Model)))return false;
                return File.Exists(Path.Combine(root,"runtime/translation/avx2/llama-server.exe"))||File.Exists(Path.Combine(root,"runtime/translation/baseline/llama-server.exe"));
            }
        }
        internal object Status {
            get {
                bool running=false;int pid=0;if(Monitor.TryEnter(processGate)){try{running=server!=null&&!server.HasExited;if(running)pid=server.Id;}finally{Monitor.Exit(processGate);}}string log;lock(logGate){log=errors.ToString();}
                return new{engine="hy",model="Hy-MT2-1.8B-1.25bit",warmed=running&&ready,threads=Hardware.InferenceThreads,logical_threads=Hardware.LogicalThreads,physical_cores=Hardware.PhysicalCores,cpu_isa=isa,device=device,beam_size=1,inference_pid=pid,backend=running&&ready?(device=="CPU"?"llama.cpp STQ / CPU":"llama.cpp STQ / GPU"):"warming",error=log};
            }
        }
        internal void Prewarm(Func<bool> canceled){if(!Monitor.TryEnter(inferenceGate))return;try{Start(canceled);if(Volatile.Read(ref pendingRequests)==0&&!canceled())Json.Request(url+"/v1/chat/completions",new{messages=new[]{new{role="user",content=Prompt("Hello.","zh")}},max_tokens=1,temperature=0,top_k=1,cache_prompt=true,stream=false},key,15,delegate{return canceled()||Volatile.Read(ref pendingRequests)>0;});}catch(OperationCanceledException){}catch(Exception error){AddError(error.Message);}finally{Monitor.Exit(inferenceGate);}}
        static string Prompt(string text,string target){if(target=="zh")return "将以下文本翻译为简体中文，注意只需要输出翻译后的结果，不要额外解释：\n\n"+text;if(target=="zh-Hant")return "将以下文本翻译为繁体中文，注意只需要输出繁体中文翻译后的结果，不要额外解释：\n\n"+text;return "Translate the following text into "+Translation.Languages[target]+". Note that you should only output the translated result without any additional explanation:\n\n"+text;}
        void ForegroundPriority(){try{if(server!=null&&!server.HasExited)server.PriorityClass=ProcessPriorityClass.Normal;}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
        static string Quote(string value){return "\""+value.Replace("\"","")+"\"";}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void AddError(string value){lock(logGate){errors.Append(value).Append('\n');if(errors.Length>6000)errors.Remove(0,errors.Length-6000);}}
        void Start(Func<bool> canceled) {
            if(disposed)throw new ObjectDisposedException("MiniAI Hy translation");
            lock(processGate){
                if(server!=null&&!server.HasExited)return;
                if(server!=null){server.Dispose();server=null;}if(group!=null){group.Dispose();group=null;}
                lock(logGate){errors.Length=0;}
                string preference=gpuFailed?"cpu":Environment.GetEnvironmentVariable("MINIAI_DEVICE")??"auto";string[] variants=IsProcessorFeaturePresent(40)?(preference=="cpu"?new[]{"avx2","baseline"}:new[]{"vulkan","avx2","baseline"}):(preference=="cpu"?new[]{"baseline"}:new[]{"vulkan","baseline"});
                foreach(string variant in variants){
                    string executable=Path.Combine(root,"runtime/translation",variant,"llama-server.exe");if(!File.Exists(executable))continue;
                    try{NativeDependencies.Check(executable);}catch(Exception error){AddError(error.Message);continue;}
                    string selectedDevice=variant=="vulkan"?DetectDevice(executable,preference):"CPU";if(variant=="vulkan"&&selectedDevice=="CPU")continue;int port=FreePort();key=Json.Token();url="http://127.0.0.1:"+port;isa=variant=="avx2"?"AVX2":"baseline";device=selectedDevice;
                    int requestedLayers;string layers=Int32.TryParse(Environment.GetEnvironmentVariable("MINIAI_GPU_LAYERS"),out requestedLayers)&&requestedLayers>=0?Math.Min(1000,requestedLayers).ToString():"auto";ready=false;string acceleration=selectedDevice=="CPU"?"":" --device "+Quote(selectedDevice)+" --n-gpu-layers "+layers+" --no-mmap";
                    string flash=Environment.GetEnvironmentVariable("MINIAI_FLASH_ATTENTION");if(flash!="on"&&flash!="off"&&flash!="auto")flash="auto";
                    var info=new ProcessStartInfo(executable,"-m "+Quote(Path.Combine(root,Model))+" --ctx-size 4096 --threads "+Hardware.InferenceThreads+" --threads-batch "+Hardware.InferenceThreads+" --threads-http 2 --batch-size 512 --ubatch-size 256 --cache-reuse 256 --flash-attn "+flash+" --parallel 1 --host 127.0.0.1 --port "+port+" --api-key "+Quote(key)+" --jinja --temp 0 --top-k 1 --top-p 1 --repeat-penalty 1.05"+acceleration){
                        UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                        RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,
                        WorkingDirectory=Path.GetDirectoryName(executable)
                    };
                    info.EnvironmentVariables["PATH"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32")+";"+Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if(selectedDevice!="CPU")info.EnvironmentVariables["MINIAI_STQ_FP16"]="gpu";
                    var process=new Process{StartInfo=info};process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)AddError(e.Data);};process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)AddError(e.Data);};
                    try{process.Start();server=process;try{process.PriorityClass=Volatile.Read(ref pendingRequests)>0?ProcessPriorityClass.Normal:ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}group=new ProcessGroup(process);process.BeginOutputReadLine();process.BeginErrorReadLine();}
                    catch(Exception error){try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();AddError(error.Message);server=null;continue;}
                    var watch=Stopwatch.StartNew();
                    while(watch.ElapsedMilliseconds<(variant=="vulkan"?30000:90000)&&!process.HasExited){
                        if(canceled()){Stop();throw new OperationCanceledException();}
                        try{var health=Json.Request(url+"/health",null,key,2);if(Json.Get(health,"status")=="ok"){ready=true;lastUse=DateTime.UtcNow;return;}}
                        catch(WebException){}catch(IOException){}
                        Thread.Sleep(200);
                    }
                    Stop();
                }
            }
            throw new IOException("The compatible Hy/STQ translation engine could not start. "+errors.ToString());
        }
        internal static bool IntegratedDevice(string description){return System.Text.RegularExpressions.Regex.IsMatch(description,@"(?i)(Intel.*(?:UHD|HD Graphics|Iris)|AMD Radeon(?:\(TM\))? Graphics|Radeon.*(?:Vega|[6789]80M)|llvmpipe|lavapipe|SwiftShader|software|Microsoft Basic Render)");}
        static string DetectDevice(string executable,string preference){
            try {
                var info=new ProcessStartInfo(executable,"--list-devices"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(executable)};
                using(var process=new Process{StartInfo=info}){var output=new StringBuilder();process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs args){if(args.Data!=null)lock(output){if(output.Length<32000)output.AppendLine(args.Data);}};process.ErrorDataReceived+=delegate{};process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();if(!process.WaitForExit(5000)){try{process.Kill();process.WaitForExit(1000);}catch(InvalidOperationException){}return "CPU";}process.WaitForExit();string text;lock(output){text=output.ToString();}foreach(string line in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){var match=System.Text.RegularExpressions.Regex.Match(line.Trim(),@"^(Vulkan|CUDA|ROCm|SYCL)\d+:");if(!match.Success)continue;string id=match.Value.TrimEnd(':');if(preference=="gpu"||String.Equals(preference,id,StringComparison.OrdinalIgnoreCase)||preference=="auto"&&!IntegratedDevice(line))return id;}}
            }catch(Exception){}
            return "CPU";
        }
        internal object Translate(string text,string target,Func<bool> canceled,Action<string> progress,string mode) {
            Interlocked.Increment(ref pendingRequests);bool entered=false;
            try {
                while(!(entered=Monitor.TryEnter(inferenceGate,40))){if(canceled()||disposed)throw new OperationCanceledException();}
                var chunks=MiniAI.Desktop.TranslationText.Chunks(text,1500);var all=new StringBuilder();Dictionary<string,object> combined=null;bool allCached=true;int reused=0;
                foreach(var chunk in chunks){string previous=all.ToString();var part=(Dictionary<string,object>)TranslateRecoverable(chunk.Text,target,canceled,progress==null?null:new Action<string>(delegate(string partial){progress(previous+partial);}),mode);all.Append(Convert.ToString(part["text"])).Append(chunk.After);allCached&=Convert.ToBoolean(part["cached"]);reused+=Convert.ToInt32(part["cached_prompt_tokens"]);combined=part;if(Convert.ToBoolean(part["truncated"]))break;}
                Guard.Check(combined!=null,"Provide text to translate.");combined=new Dictionary<string,object>(combined);combined["text"]=all.ToString().TrimEnd();combined["cached"]=allCached;combined["cached_prompt_tokens"]=reused;combined["chunks"]=chunks.Count;return combined;
            }
            finally {Interlocked.Decrement(ref pendingRequests);lastUse=DateTime.UtcNow;if(entered)Monitor.Exit(inferenceGate);}
        }
        object TranslateRecoverable(string text,string target,Func<bool> canceled,Action<string> progress,string mode){
            try{return TranslateCore(text,target,canceled,progress,mode);}
            catch(Exception error){if(!(error is IOException||error is WebException)||device=="CPU"||canceled()||disposed)throw;gpuFailed=true;Stop();return TranslateCore(text,target,canceled,progress,mode);}
        }
        object TranslateCore(string text,string target,Func<bool> canceled,Action<string> progress,string mode) {
            string cacheKey=target+"\0"+mode+"\0"+text;Dictionary<string,object> cached;
            if(canceled()||disposed)throw new OperationCanceledException();
            if(cache.TryGetValue(cacheKey,out cached)){var hit=new Dictionary<string,object>(cached);hit["cached"]=true;if(progress!=null)progress(Convert.ToString(hit["text"]));return hit;}
            if(canceled())throw new OperationCanceledException();Start(canceled);ForegroundPriority();
            var request=(HttpWebRequest)WebRequest.Create(url+"/v1/chat/completions");request.Proxy=null;request.KeepAlive=false;request.Timeout=request.ReadWriteTimeout=600000;request.Method="POST";request.ContentType="application/json; charset=utf-8";request.Headers[HttpRequestHeader.Authorization]="Bearer "+key;
            int maxTokens=Math.Max(64,Math.Min(2048,text.Length*2+48));bool quality=mode=="quality";
            string prompt=Prompt(text,target);
            byte[] bytes=Encoding.UTF8.GetBytes(Json.Text(new{messages=new[]{new{role="user",content=prompt}},max_tokens=maxTokens,temperature=quality?0.1:0,top_p=quality?0.6:1,top_k=quality?40:1,repeat_penalty=1.05,cache_prompt=true,stream=true,stream_options=new{include_usage=true}}));request.ContentLength=bytes.Length;
            var result=new StringBuilder();string finish="";bool completed=false;int cachedTokens=0;
            using(var timer=new Timer(delegate{try{if(canceled())request.Abort();}catch(ObjectDisposedException){}},null,100,100))
            try {
                using(var input=request.GetRequestStream())input.Write(bytes,0,bytes.Length);
                using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){
                    string line;while((line=reader.ReadLine())!=null){
                        if(canceled())throw new OperationCanceledException();if(line.Length>1024*1024)throw new IOException("Hy translation response exceeds the limit.");
                        if(!line.StartsWith("data:",StringComparison.Ordinal))continue;string data=line.Substring(5).Trim();if(data=="[DONE]"){completed=true;break;}if(data.Length==0)continue;
                        Dictionary<string,object> item=Json.Read(data);object usageValue;
                        if(item.TryGetValue("usage",out usageValue)){var usage=usageValue as Dictionary<string,object>;object detailValue;if(usage!=null&&usage.TryGetValue("prompt_tokens_details",out detailValue)){var details=detailValue as Dictionary<string,object>;object tokens;if(details!=null&&details.TryGetValue("cached_tokens",out tokens))cachedTokens=Convert.ToInt32(tokens);}}
                        object choicesValue;if(!item.TryGetValue("choices",out choicesValue))continue;var choices=choicesValue as IList;if(choices==null||choices.Count==0)continue;var choice=choices[0] as Dictionary<string,object>;if(choice==null)continue;
                        object reason;if(choice.TryGetValue("finish_reason",out reason)&&reason!=null)finish=Convert.ToString(reason);
                        object deltaValue;if(!choice.TryGetValue("delta",out deltaValue))continue;var delta=deltaValue as Dictionary<string,object>;if(delta==null)continue;object contentValue;
                        if(delta.TryGetValue("content",out contentValue)&&contentValue!=null){string part=Convert.ToString(contentValue);if(part.Length>0){result.Append(part);if(progress!=null)progress(result.ToString());}}
                    }
                }
            }catch(WebException error){if(canceled())throw new OperationCanceledException();var response=error.Response as HttpWebResponse;if(response!=null)using(response)using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string message=reader.ReadToEnd();throw new IOException("Hy translation server: "+message,error);}throw;}
            if(canceled())throw new OperationCanceledException();Guard.Check(completed&&finish.Length>0,"Hy translation ended before the server completed the response.");string translated=result.ToString().Trim();Guard.Check(translated.Length>0,"Hy-MT2 returned an empty translation.");
            var output=new Dictionary<string,object>{{"text",translated},{"target",target},{"model","Hy-MT2-1.25-bit"},{"engine","hy"},{"backend","llama.cpp STQ / "+isa},{"cpu_isa",isa},{"device",device},{"threads",Hardware.InferenceThreads},{"logical_threads",Hardware.LogicalThreads},{"physical_cores",Hardware.PhysicalCores},{"beam_size",1},{"decode_strategy",quality?"sampling":"greedy"},{"mode",mode},{"cached",false},{"truncated",finish=="length"},{"cached_prompt_tokens",cachedTokens}};
            lastUse=DateTime.UtcNow;if(finish!="length"){while(cache.Count>=64){string old=cacheOrder.Dequeue();cache.Remove(old);}cache[cacheKey]=new Dictionary<string,object>(output);cacheOrder.Enqueue(cacheKey);}
            return output;
        }
        void Stop(){lock(processGate){ready=false;if(group!=null){group.Dispose();group=null;}if(server!=null){try{if(!server.HasExited)server.Kill();server.WaitForExit(1000);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}server.Dispose();server=null;}url=null;key=null;}}
        internal void StopIfIdle(bool force){if(force||server!=null&&(DateTime.UtcNow-lastUse).TotalMinutes>=5)Stop();}
        internal void ReleaseIfIdle(){if(!Monitor.TryEnter(inferenceGate))return;try{StopIfIdle(false);}finally{Monitor.Exit(inferenceGate);}}
        public void Dispose(){disposed=true;Stop();}
    }
}
