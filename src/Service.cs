using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MiniAI {
    internal static class Guard {internal static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}}
    internal static class Json {
        internal static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=32};}
        internal static string Text(object value){return Serializer().Serialize(value);}
        internal static Dictionary<string,object> Read(string value){return Serializer().Deserialize<Dictionary<string,object>>(value);}
        internal static string Get<T>(IDictionary<string,T> data,string key,string fallback=""){T value;return data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):fallback;}
        internal static Dictionary<string,object> Request(string url,object body,string token,int seconds=300,Func<bool> canceled=null) {
            var request=(HttpWebRequest)WebRequest.Create(url);request.Proxy=null;request.Timeout=request.ReadWriteTimeout=seconds*1000;request.KeepAlive=false;
            if(!String.IsNullOrEmpty(token))request.Headers["Authorization"]="Bearer "+token;
            if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(Text(body));request.Method="POST";request.ContentType="application/json; charset=utf-8";request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
            using(var timer=canceled==null?null:new System.Threading.Timer(delegate{if(canceled())request.Abort();},null,200,200))
            try{using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return Read(reader.ReadToEnd());}
            catch(WebException){if(canceled!=null&&canceled())throw new OperationCanceledException();throw;}
        }
        internal static string Token(){var bytes=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);return Convert.ToBase64String(bytes);}
    }
    internal sealed class Service : IDisposable {
        static bool Disconnected(TcpClient client){try{return client.Client==null||client.Client.Poll(0,SelectMode.SelectRead)&&client.Client.Available==0;}catch(SocketException){return true;}catch(ObjectDisposedException){return true;}}
        readonly string root,token;
        readonly TcpListener listener;
        readonly SemaphoreSlim slots=new SemaphoreSlim(2,2);
        readonly object ocrGate=new object();
        readonly Translation translation;
        readonly System.Threading.Timer idle;
        readonly bool autoStop;
        OfflineOcr ocr;
        DateTime lastRequest=DateTime.UtcNow,lastOcr;
        int active;
        volatile bool stopped;
        internal readonly int Port;
        internal string Address {get{return "http://127.0.0.1:"+Port;}}
        internal string Token {get{return token;}}
        bool HasFiles(params string[] paths){foreach(string path in paths)if(!File.Exists(Path.Combine(root,path)))return false;return true;}
        internal Service(string directory,int port,bool unattended) {
            autoStop=unattended;
            root=Path.GetFullPath(directory);token=Json.Token();translation=new Translation(root);
            listener=new TcpListener(IPAddress.Loopback,port);listener.Start(16);Port=((IPEndPoint)listener.LocalEndpoint).Port;
            string state=Path.Combine(root,"state");Directory.CreateDirectory(state);
            string connection=Path.Combine(state,"connection.json"),pending=connection+".new";
            File.WriteAllText(pending,Json.Text(new{api_version=1,url=Address,token=token,pid=Process.GetCurrentProcess().Id,root=root}),new UTF8Encoding(false));
            if(File.Exists(connection))File.Replace(pending,connection,null);else File.Move(pending,connection);
            idle=new System.Threading.Timer(delegate{if(Volatile.Read(ref active)!=0)return;lock(ocrGate){if(ocr!=null&&(DateTime.UtcNow-lastOcr).TotalMinutes>=5){ocr.Dispose();ocr=null;}}translation.ReleaseIfIdle();if(autoStop&&(DateTime.UtcNow-lastRequest).TotalMinutes>=10)Dispose();},null,30000,30000);
        }
        internal void Run(){while(!stopped){try{var socket=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(delegate{using(socket)Handle(socket);});}catch(SocketException){if(!stopped)throw;}catch(ObjectDisposedException){if(!stopped)throw;}}}
        static string Line(Stream stream,int limit){var bytes=new List<byte>();while(bytes.Count<limit){int c=stream.ReadByte();if(c<0)throw new EndOfStreamException();if(c==10)return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');bytes.Add((byte)c);}throw new IOException("HTTP header exceeds the limit.");}
        bool OriginAllowed(string origin){if(String.IsNullOrEmpty(origin))return true;string path=Path.Combine(root,"allowed-origins.txt");if(!File.Exists(path))return false;foreach(string line in File.ReadAllLines(path))if(String.Equals(line.Trim(),origin,StringComparison.Ordinal))return true;return false;}
        static bool Equal(string a,string b){if(a==null||b==null)return false;int difference=a.Length^b.Length;for(int i=0;i<a.Length;i++)difference|=a[i]^b[i%b.Length];return difference==0;}
        void Reply(Stream stream,int status,object body,string origin) {
            byte[] bytes=Encoding.UTF8.GetBytes(Json.Text(body));string header="HTTP/1.1 "+status+" "+(status==200?"OK":"Error")+"\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+bytes.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";
            if(!String.IsNullOrEmpty(origin)&&OriginAllowed(origin))header+="Access-Control-Allow-Origin: "+origin+"\r\nVary: Origin\r\nAccess-Control-Allow-Methods: POST, GET, OPTIONS\r\nAccess-Control-Allow-Headers: Authorization, Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
            var head=Encoding.ASCII.GetBytes(header+"\r\n");stream.Write(head,0,head.Length);stream.Write(bytes,0,bytes.Length);stream.Flush();
        }
        void Handle(TcpClient client) {
            client.ReceiveTimeout=10000;client.SendTimeout=15000;string origin="";bool entered=false;
            using(var stream=client.GetStream())try {
                string[] start=Line(stream,4096).Split(' ');Guard.Check(start.Length==3&&start[2]=="HTTP/1.1","Unsupported HTTP request.");
                var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int total=0;
                while(true){string line=Line(stream,8192);total+=line.Length;Guard.Check(total<=32768,"HTTP headers exceed the limit.");if(line.Length==0)break;int split=line.IndexOf(':');Guard.Check(split>0,"Invalid HTTP header.");string key=line.Substring(0,split);Guard.Check(!headers.ContainsKey(key),"Duplicate HTTP header.");headers[key]=line.Substring(split+1).Trim();}
                string host=Json.Get(headers,"Host"),authorization=Json.Get(headers,"Authorization");origin=Json.Get(headers,"Origin");
                if(host!="127.0.0.1:"+Port&&host!="localhost:"+Port){Reply(stream,403,new{error="Invalid local host."},"");return;}
                if(!OriginAllowed(origin)){Reply(stream,403,new{error="This browser origin is not enabled."},"");return;}
                if(start[0]=="OPTIONS"){Reply(stream,200,new{ok=true},origin);return;}
                if(!Equal(authorization,"Bearer "+token)){Reply(stream,401,new{error="A valid MiniAI access token is required."},origin);return;}
                if(start[0]=="GET"&&start[1]=="/health"){Reply(stream,200,new{ok=true,api_version=1,name="MiniAI",ocr=HasFiles("models/ocr/rec.onnx","models/ocr/det.onnx","models/ocr/keys.txt","runtime/onnxruntime.dll"),translation=HasFiles("models/translation/hy-mt2.gguf")&&(HasFiles("runtime/translation/avx2/llama-server.exe")||HasFiles("runtime/translation/baseline/llama-server.exe"))},origin);return;}
                if(start[1]!="/v1/shutdown"){if(!slots.Wait(0)){Reply(stream,429,new{error="MiniAI is busy. Please retry shortly."},origin);return;}entered=true;Interlocked.Increment(ref active);}lastRequest=DateTime.UtcNow;
                if(start[0]!="POST"){Reply(stream,405,new{error="Use POST for this endpoint."},origin);return;}
                int length=0;Guard.Check(!headers.ContainsKey("Transfer-Encoding")&&Int32.TryParse(Json.Get(headers,"Content-Length"),out length)&&length>0&&length<=32*1024*1024,"A bounded Content-Length is required.");
                Guard.Check(Json.Get(headers,"Content-Type").StartsWith("application/json",StringComparison.OrdinalIgnoreCase),"Use application/json.");
                var bytes=new byte[length];int read=0;while(read<length){int got=stream.Read(bytes,read,length-read);if(got==0)throw new EndOfStreamException();read+=got;}var request=Json.Read(Encoding.UTF8.GetString(bytes));
                if(start[1]=="/v1/ocr") {
                    string encoded=Json.Get(request,"image_base64");Guard.Check(encoded.Length>0&&encoded.Length<=24*1024*1024,"Provide an image_base64 string of at most 24 MiB.");
                    using(var input=new MemoryStream(Convert.FromBase64String(encoded)))using(var image=Image.FromStream(input,false,false)) {
                        Guard.Check(image.Width>0&&image.Height>0&&(long)image.Width*image.Height<=24000000,"Image exceeds 24 megapixels.");
                        using(var bitmap=new Bitmap(image))lock(ocrGate){if(ocr==null)ocr=new OfflineOcr(root);lastOcr=DateTime.UtcNow;var lines=ocr.Read(bitmap,delegate(int done,int all){if(stopped||Disconnected(client))throw new OperationCanceledException();});var result=new List<object>();var text=new StringBuilder();foreach(var line in lines){if(text.Length>0)text.AppendLine();text.Append(line.Text);var box=new List<float[]>();foreach(var point in line.Corners)box.Add(new[]{point.X,point.Y});result.Add(new{text=line.Text,confidence=line.Confidence,box=box});}Reply(stream,200,new{text=text.ToString(),width=image.Width,height=image.Height,lines=result},origin);}
                    }
                } else if(start[1]=="/v1/translate") {string text=Json.Get(request,"text"),target=Json.Get(request,"target");Guard.Check(!String.IsNullOrWhiteSpace(text)&&text.Length<=10000,"Provide 1 to 10000 characters of text.");Guard.Check(Translation.Languages.ContainsKey(target),"Unsupported translation target.");Reply(stream,200,translation.Translate(text,target,delegate{return stopped||Disconnected(client);}),origin);}
                else if(start[1]=="/v1/shutdown"){Reply(stream,200,new{ok=true},origin);ThreadPool.QueueUserWorkItem(delegate{Dispose();});}
                else Reply(stream,404,new{error="Unknown MiniAI endpoint."},origin);
            }catch(Exception error){try{Reply(stream,400,new{error=error.Message},origin);}catch(IOException){}catch(ObjectDisposedException){}}
            finally{if(entered){lastRequest=DateTime.UtcNow;Interlocked.Decrement(ref active);slots.Release();}}
        }
        public void Dispose(){if(stopped)return;stopped=true;listener.Stop();if(idle!=null)idle.Dispose();translation.Dispose();lock(ocrGate){if(ocr!=null){ocr.Dispose();ocr=null;}}}
    }
    internal sealed class Translation : IDisposable {
        internal static readonly Dictionary<string,string> Languages=new Dictionary<string,string>{{"zh","Chinese"},{"en","English"},{"ja","Japanese"},{"ko","Korean"},{"fr","French"},{"de","German"},{"es","Spanish"},{"ru","Russian"},{"pt","Portuguese"},{"it","Italian"},{"zh-Hant","Traditional Chinese"}};
        readonly Dictionary<string,string> cache=new Dictionary<string,string>();
        readonly Queue<string> cacheOrder=new Queue<string>();
        readonly string root;
        readonly object gate=new object(),workerGate=new object();
        Process worker;
        ProcessGroup group;
        volatile bool disposed;
        string url,key,lastError="";
        DateTime lastUse;
        internal Translation(string directory){root=directory;}
        static string Quote(string value){return "\""+value.Replace("\"","")+"\"";}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void Start(Func<bool> canceled) {
            if(disposed)throw new ObjectDisposedException("MiniAI translation");
            if(worker!=null&&!worker.HasExited)return;
            if(worker!=null){worker.Dispose();worker=null;}
            foreach(string variant in new[]{"avx2","baseline"}) {
                string executable=Path.Combine(root,"runtime","translation",variant,"llama-server.exe");if(!File.Exists(executable))continue;
                int port=FreePort();key=Json.Token();url="http://127.0.0.1:"+port;int threads=Math.Max(1,Math.Min(8,Environment.ProcessorCount));
                var info=new ProcessStartInfo(executable,"-m "+Quote(Path.Combine(root,"models","translation","hy-mt2.gguf"))+" -c 4096 -t "+threads+" -ngl 0 --parallel 1 --host 127.0.0.1 --port "+port+" --api-key "+Quote(key)+" --jinja"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                lastError="";var process=new Process{StartInfo=info};process.OutputDataReceived+=delegate{};process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null){lock(this){lastError=(lastError+e.Data+"\n");if(lastError.Length>4096)lastError=lastError.Substring(lastError.Length-4096);}}};
                lock(workerGate){if(disposed)throw new ObjectDisposedException("MiniAI translation");worker=process;process.Start();group=new ProcessGroup(process);process.BeginOutputReadLine();process.BeginErrorReadLine();}
                var watch=Stopwatch.StartNew();while(!process.HasExited&&watch.ElapsedMilliseconds<90000){if(canceled()){Stop();throw new OperationCanceledException();}try{var health=Json.Request(url+"/health",null,key,2);if(Json.Get(health,"status")=="ok")return;}catch(WebException){}Thread.Sleep(200);}
                Stop();
            }
            throw new IOException("The STQ translation engine did not start. Install the tested MiniAI Windows runtime. "+lastError);
        }
        internal object Translate(string text,string target,Func<bool> canceled) {
            lock(gate) {
                string cacheKey=target+"\0"+text,cached;if(cache.TryGetValue(cacheKey,out cached))return new{text=cached,target=target,model="Hy-MT2-1.8B-1.25bit",truncated=false,cached=true,kv_cached_tokens=0};
                if(canceled())throw new OperationCanceledException();Start(canceled);lastUse=DateTime.UtcNow;
                string prompt="Translate the following text into "+Languages[target]+". Note that you should only output the translated result without any additional explanation:\n\n"+text;
                var reply=Json.Request(url+"/v1/chat/completions",new{messages=new[]{new{role="user",content=prompt}},max_tokens=2048,temperature=0.1,top_p=0.6,repeat_penalty=1.05,cache_prompt=true,stream=false},key,600,canceled);
                var choices=(IList)reply["choices"];var choice=(Dictionary<string,object>)choices[0];var message=(Dictionary<string,object>)choice["message"];string result=Json.Get(message,"content").Trim();Guard.Check(result.Length>0,"The model returned an empty translation.");lastUse=DateTime.UtcNow;
                int reused=0;object usageObject,detailsObject,tokensObject;if(reply.TryGetValue("usage",out usageObject)){var usage=usageObject as Dictionary<string,object>;if(usage!=null&&usage.TryGetValue("prompt_tokens_details",out detailsObject)){var details=detailsObject as Dictionary<string,object>;if(details!=null&&details.TryGetValue("cached_tokens",out tokensObject))reused=Convert.ToInt32(tokensObject);}}
                bool truncated=Json.Get(choice,"finish_reason")=="length";if(!truncated){while(cache.Count>=32)cache.Remove(cacheOrder.Dequeue());cache[cacheKey]=result;cacheOrder.Enqueue(cacheKey);}return new{text=result,target=target,model="Hy-MT2-1.8B-1.25bit",truncated=truncated,cached=false,kv_cached_tokens=reused};
            }
        }
        void Stop(){lock(workerGate){if(group!=null){group.Dispose();group=null;}if(worker!=null){try{if(!worker.HasExited)worker.Kill();worker.WaitForExit(1000);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}worker.Dispose();worker=null;}}}
        internal void ReleaseIfIdle(){if(!Monitor.TryEnter(gate))return;try{if(worker!=null&&(DateTime.UtcNow-lastUse).TotalMinutes>=5)Stop();}finally{Monitor.Exit(gate);}}
        public void Dispose(){disposed=true;Stop();}
    }
}
