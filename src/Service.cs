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
        readonly object stopGate=new object();
        readonly Translation translation;
        readonly System.Threading.Timer idle;
        readonly bool autoStop;
        readonly int ownerPid;
        readonly ManualResetEvent ended=new ManualResetEvent(false);
        volatile bool retiring;
        int timerRunning;
        OfflineOcr ocr,fastOcr;
        FormulaOcr formula;
        DateTime lastRequest=DateTime.UtcNow,lastOcr;
        int active;
        volatile bool stopped;
        internal readonly int Port;
        internal string Address {get{return "http://127.0.0.1:"+Port;}}
        internal string Token {get{return token;}}
        bool HasFiles(params string[] paths){foreach(string path in paths)if(!File.Exists(Path.Combine(root,path)))return false;return true;}
        internal Service(string directory,int port,bool unattended) {
            autoStop=unattended;
            int.TryParse(Environment.GetEnvironmentVariable("MINIAI_OWNER_PID"),out ownerPid);
            root=Path.GetFullPath(directory);token=Json.Token();translation=new Translation(root);
            listener=new TcpListener(IPAddress.Loopback,port);listener.Start(16);Port=((IPEndPoint)listener.LocalEndpoint).Port;
            string state=Path.Combine(root,"state");Directory.CreateDirectory(state);
            string connection=Path.Combine(state,"connection.json"),pending=connection+".new";
            File.WriteAllText(pending,Json.Text(new{api_version=1,url=Address,token=token,pid=Process.GetCurrentProcess().Id,root=root}),new UTF8Encoding(false));
            if(File.Exists(connection))File.Replace(pending,connection,null);else File.Move(pending,connection);
            translation.Prewarm();
            idle=new System.Threading.Timer(delegate{if(Interlocked.Exchange(ref timerRunning,1)!=0)return;try{if(Volatile.Read(ref active)!=0)return;lock(ocrGate){if((DateTime.UtcNow-lastOcr).TotalMinutes>=5){if(ocr!=null){ocr.Dispose();ocr=null;}if(fastOcr!=null){fastOcr.Dispose();fastOcr=null;}if(formula!=null){formula.Dispose();formula=null;}}}translation.ReleaseIfIdle();if(!ClientLease.Any(root)&&(retiring||autoStop&&((DateTime.UtcNow-lastRequest).TotalMinutes>=10||ownerPid>0&&!Alive(ownerPid))))Dispose();}finally{Volatile.Write(ref timerRunning,0);}},null,1000,1000);
        }
        static bool Alive(int pid){try{using(var process=Process.GetProcessById(pid))return !process.HasExited;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return true;}}
        internal void Retire(){retiring=true;if(Volatile.Read(ref active)==0&&!ClientLease.Any(root))Dispose();}
        internal void Wait(){ended.WaitOne();}
        internal void Run(){while(!stopped){try{var socket=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(delegate{using(socket)Handle(socket);});}catch(SocketException){if(!stopped)throw;}catch(ObjectDisposedException){if(!stopped)throw;}}}
        static string Line(Stream stream,int limit){var bytes=new List<byte>();while(bytes.Count<limit){int c=stream.ReadByte();if(c<0)throw new EndOfStreamException();if(c==10)return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');bytes.Add((byte)c);}throw new IOException("HTTP header exceeds the limit.");}
        bool OriginAllowed(string origin){if(String.IsNullOrEmpty(origin))return true;string path=Path.Combine(root,"allowed-origins.txt");if(!File.Exists(path))return false;foreach(string line in File.ReadAllLines(path))if(String.Equals(line.Trim(),origin,StringComparison.Ordinal))return true;return false;}
        static bool Equal(string a,string b){if(a==null||b==null)return false;int difference=a.Length^b.Length;for(int i=0;i<a.Length;i++)difference|=a[i]^b[i%b.Length];return difference==0;}
        static RectangleF Box(OcrLine line){float left=1,top=1,right=0,bottom=0;foreach(PointF point in line.Corners){left=Math.Min(left,point.X);right=Math.Max(right,point.X);top=Math.Min(top,point.Y);bottom=Math.Max(bottom,point.Y);}return RectangleF.FromLTRB(left,top,right,bottom);}
        static bool FormulaJoinable(OcrLine a,OcrLine b){RectangleF x=Box(a),y=Box(b);float overlap=Math.Min(x.Right,y.Right)-Math.Max(x.Left,y.Left),width=Math.Min(x.Width,y.Width),gap=Math.Max(0,Math.Max(x.Top,y.Top)-Math.Min(x.Bottom,y.Bottom));return overlap>Math.Max(0.01f,width*0.25f)&&gap<Math.Max(x.Height,y.Height)*0.8f;}
        static PointF[] UnionCorners(OcrLine a,OcrLine b){RectangleF x=Box(a),y=Box(b);return new[]{new PointF(Math.Min(x.Left,y.Left),Math.Min(x.Top,y.Top)),new PointF(Math.Max(x.Right,y.Right),Math.Min(x.Top,y.Top)),new PointF(Math.Max(x.Right,y.Right),Math.Max(x.Bottom,y.Bottom)),new PointF(Math.Min(x.Left,y.Left),Math.Max(x.Bottom,y.Bottom))};}
        static bool HasHorizontalRule(Bitmap image,PointF[] normalized){RectangleF box=Box(new OcrLine{Corners=normalized});int left=Math.Max(0,(int)(box.Left*image.Width)),right=Math.Min(image.Width-1,(int)(box.Right*image.Width)),top=Math.Max(0,(int)(box.Top*image.Height)),bottom=Math.Min(image.Height-1,(int)(box.Bottom*image.Height));int width=Math.Max(1,right-left+1);for(int y=top;y<=bottom;y++){int run=0,best=0;for(int x=left;x<=right;x++){Color c=image.GetPixel(x,y);int gray=(c.R*30+c.G*59+c.B*11)/100;if(gray<150){run++;best=Math.Max(best,run);}else run=0;}if(best>=Math.Max(8,(int)(width*0.35)))return true;}return false;}
        List<OcrLine> ReadImage(Bitmap bitmap,string mode,bool wantFormula,bool formulaOnly,Func<bool> canceled,out bool formulaReady) {
            lastOcr=DateTime.UtcNow;formulaReady=false;
            if(wantFormula&&FormulaOcr.Available(root))try{if(formula==null)formula=new FormulaOcr(root);formulaReady=true;}catch(IOException){if(formula!=null){formula.Dispose();formula=null;}}
            if(formulaOnly&&formulaReady){string tex;float confidence;var box=new[]{new PointF(0,0),new PointF(1,0),new PointF(1,1),new PointF(0,1)};if(!formula.TryRead(bitmap,box,out tex,out confidence,canceled))return new List<OcrLine>();return new List<OcrLine>{new OcrLine{Text=tex,Tex=tex,Kind="formula",FormulaConfidence=confidence,Confidence=confidence,Corners=box}};}
            OfflineOcr engine;if(mode=="fast"){if(fastOcr==null)fastOcr=new OfflineOcr(root,"fast");engine=fastOcr;}else{if(ocr==null)ocr=new OfflineOcr(root);engine=ocr;}
            var lines=engine.Read(bitmap,delegate(int done,int all){if(canceled())throw new OperationCanceledException();});if(!formulaReady)return lines;
            for(int i=0;i<lines.Count;i++){
                if(canceled())throw new OperationCanceledException();if(!FormulaOcr.IsCandidate(lines[i].Text))continue;int end=i+1;PointF[] merged=lines[i].Corners;
                while(end<lines.Count&&FormulaOcr.IsCandidate(lines[end].Text)&&FormulaJoinable(lines[end-1],lines[end])){var union=new OcrLine{Corners=merged};merged=UnionCorners(union,lines[end]);end++;}
                string raw=lines[i].Text;for(int n=i+1;n<end;n++)raw+=" "+lines[n].Text;if(!FormulaOcr.IsMathGroup(raw,end-i))continue;
                try{string tex;float confidence;bool fraction=end-i==2&&lines[i].Text.Trim().Length==1&&lines[i+1].Text.Trim().Length==1&&HasHorizontalRule(bitmap,merged);
                    if(fraction){tex="\\frac{"+lines[i].Text.Trim()+"}{"+lines[i+1].Text.Trim()+"}";confidence=1;}
                    else if(!formula.TryRead(bitmap,merged,out tex,out confidence,canceled))continue;
                    lines[i].Kind="formula";lines[i].Tex=FormulaOcr.Stabilize(raw,tex);lines[i].FormulaConfidence=confidence;lines[i].Corners=merged;for(int remove=end-1;remove>i;remove--)lines.RemoveAt(remove);
                }catch(OperationCanceledException){throw;}catch(IOException){/* Keep ordinary OCR if an optional crop fails. */}catch(InvalidOperationException){}
            }return lines;
        }
        void Reply(Stream stream,int status,object body,string origin) {
            byte[] bytes=Encoding.UTF8.GetBytes(Json.Text(body));string header="HTTP/1.1 "+status+" "+(status==200?"OK":"Error")+"\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+bytes.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";
            if(!String.IsNullOrEmpty(origin)&&OriginAllowed(origin))header+="Access-Control-Allow-Origin: "+origin+"\r\nVary: Origin\r\nAccess-Control-Allow-Methods: POST, GET, OPTIONS\r\nAccess-Control-Allow-Headers: Authorization, Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
            var head=Encoding.ASCII.GetBytes(header+"\r\n");stream.Write(head,0,head.Length);stream.Write(bytes,0,bytes.Length);stream.Flush();
        }
        void StreamHeader(Stream stream,string origin) {
            string header="HTTP/1.1 200 OK\r\nContent-Type: application/x-ndjson; charset=utf-8\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";
            if(!String.IsNullOrEmpty(origin))header+="Access-Control-Allow-Origin: "+origin+"\r\nVary: Origin\r\n";
            byte[] bytes=Encoding.ASCII.GetBytes(header+"\r\n");stream.Write(bytes,0,bytes.Length);stream.Flush();
        }
        void Handle(TcpClient client) {
            client.NoDelay=true;client.ReceiveTimeout=10000;client.SendTimeout=15000;string origin="";bool entered=false;
            using(var stream=client.GetStream())try {
                string[] start=Line(stream,4096).Split(' ');Guard.Check(start.Length==3&&start[2]=="HTTP/1.1","Unsupported HTTP request.");
                var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int total=0;
                while(true){string line=Line(stream,8192);total+=line.Length;Guard.Check(total<=32768,"HTTP headers exceed the limit.");if(line.Length==0)break;int split=line.IndexOf(':');Guard.Check(split>0,"Invalid HTTP header.");string key=line.Substring(0,split);Guard.Check(!headers.ContainsKey(key),"Duplicate HTTP header.");headers[key]=line.Substring(split+1).Trim();}
                string host=Json.Get(headers,"Host"),authorization=Json.Get(headers,"Authorization");origin=Json.Get(headers,"Origin");
                if(host!="127.0.0.1:"+Port&&host!="localhost:"+Port){Reply(stream,403,new{error="Invalid local host."},"");return;}
                if(!OriginAllowed(origin)){Reply(stream,403,new{error="This browser origin is not enabled."},"");return;}
                if(start[0]=="OPTIONS"){Reply(stream,200,new{ok=true},origin);return;}
                if(!Equal(authorization,"Bearer "+token)){Reply(stream,401,new{error="A valid MiniAI access token is required."},origin);return;}
                if(start[0]=="GET"&&start[1]=="/health"){Reply(stream,200,new{ok=true,api_version=1,name="MiniAI",ocr=HasFiles("models/ocr/rec.onnx","models/ocr/det.onnx","models/ocr/keys.txt","runtime/onnxruntime.dll"),ocr_fast=HasFiles("models/ocr-fast/rec.onnx","models/ocr-fast/det.onnx","models/ocr-fast/keys.txt","runtime/onnxruntime.dll"),formula=FormulaOcr.Available(root),translation=translation.Available,translation_hy=translation.HyAvailable,translation_engine=translation.DefaultEngine,languages=MiniAI.Desktop.LanguageCatalog.Codes,inference=translation.Status},origin);return;}
                if(start[1]!="/v1/shutdown"){if(!slots.Wait(0)){Reply(stream,429,new{error="MiniAI is busy. Please retry shortly."},origin);return;}entered=true;Interlocked.Increment(ref active);}lastRequest=DateTime.UtcNow;
                if(start[0]!="POST"){Reply(stream,405,new{error="Use POST for this endpoint."},origin);return;}
                int length=0;Guard.Check(!headers.ContainsKey("Transfer-Encoding")&&Int32.TryParse(Json.Get(headers,"Content-Length"),out length)&&length>0&&length<=32*1024*1024,"A bounded Content-Length is required.");
                Guard.Check(Json.Get(headers,"Content-Type").StartsWith("application/json",StringComparison.OrdinalIgnoreCase),"Use application/json.");
                var bytes=new byte[length];int read=0;while(read<length){int got=stream.Read(bytes,read,length-read);if(got==0)throw new EndOfStreamException();read+=got;}var request=Json.Read(Encoding.UTF8.GetString(bytes));
                if(start[1]=="/v1/ocr") {
                    string mode=Json.Get(request,"mode","quality");Guard.Check(mode=="fast"||mode=="quality","Unsupported OCR mode.");if(mode=="fast"&&!HasFiles("models/ocr-fast/rec.onnx","models/ocr-fast/det.onnx","models/ocr-fast/keys.txt"))mode="quality";bool formulaOnly=Json.Get(request,"formula_only","False")=="True",wantFormula=formulaOnly||Json.Get(request,"formula","False")=="True";Guard.Check(!formulaOnly||FormulaOcr.Available(root),"The formula OCR model is missing.");
                    string encoded=Json.Get(request,"image_base64");Guard.Check(encoded.Length>0&&encoded.Length<=24*1024*1024,"Provide an image_base64 string of at most 24 MiB.");
                    using(var input=new MemoryStream(Convert.FromBase64String(encoded)))using(var image=Image.FromStream(input,false,false)) {
                        Guard.Check(image.Width>0&&image.Height>0&&(long)image.Width*image.Height<=24000000,"Image exceeds 24 megapixels.");
                        using(var bitmap=new Bitmap(image))lock(ocrGate){bool formulaReady;var lines=ReadImage(bitmap,mode,wantFormula,Json.Get(request,"formula_only","False")=="True",delegate{return stopped||Disconnected(client);},out formulaReady);var result=new List<object>();var text=new StringBuilder();int formulaCount=0;
                            foreach(var line in lines){if(text.Length>0)text.AppendLine();text.Append(line.Kind=="formula"?line.Tex:line.Text);if(line.Kind=="formula")formulaCount++;var box=new List<float[]>();foreach(var point in line.Corners)box.Add(new[]{point.X,point.Y});result.Add(new{text=line.Text,ocr_text=line.Text,kind=line.Kind,tex=line.Tex,formula_confidence=line.FormulaConfidence,confidence=line.Confidence,box=box});}
                            Reply(stream,200,new{text=text.ToString(),width=image.Width,height=image.Height,mode=mode,model=formulaOnly?"RapidLaTeX OCR":mode=="fast"?"PP-OCRv6 tiny":"PP-OCRv6 medium",formula=formulaReady,formula_count=formulaCount,lines=result},origin);
                        }
                    }
                } else if(start[1]=="/v1/translate") {string text=MiniAI.Desktop.TranslationText.Normalize(Json.Get(request,"text")),target=Json.Get(request,"target");Guard.Check(!String.IsNullOrWhiteSpace(text)&&text.Length<=10000,"Provide 1 to 10000 characters of text.");Guard.Check(Translation.Languages.ContainsKey(target),"Unsupported translation target.");string mode=Json.Get(request,"mode","fast");Guard.Check(mode=="fast"||mode=="quality","Unsupported translation mode.");string engine=Json.Get(request,"engine",translation.DefaultEngine);Guard.Check(engine=="hy","Hy-MT2 is the only bundled translation engine.");Guard.Check(translation.EngineAvailable(engine),"Install the complete Hy-MT2 offline pack.");bool streaming=Json.Get(request,"stream")=="True";
                    Func<bool> canceled=delegate{return stopped||Disconnected(client);};
                    if(!streaming)Reply(stream,200,translation.Translate(text,target,canceled,null,mode,engine),origin);
                    else {
                        bool sent=false;
                        Action<object> emit=delegate(object value){if(!sent){StreamHeader(stream,origin);sent=true;}byte[] part=Encoding.UTF8.GetBytes(Json.Text(value)+"\n");stream.Write(part,0,part.Length);stream.Flush();};
                        emit(new{type="status",stage="translating"});
                        try{var result=translation.Translate(text,target,canceled,delegate(string part){emit(new{type="partial",text=part});},mode,engine);emit(new{type="done",result=result});}
                        catch(Exception error){if(!sent)throw;try{emit(new{type="error",error=error.Message});}catch(IOException){}}
                    }}
                else if(start[1]=="/v1/results"){string path=ResultStore.Save(root,Json.Get(request,"kind"),Json.Get(request,"source"),Json.Get(request,"target"),Json.Get(request,"text"));Reply(stream,200,new{saved=true,file=path},origin);}
                else if(start[1]=="/v1/client-release"){Reply(stream,200,new{ok=true},origin);ThreadPool.QueueUserWorkItem(delegate{Retire();});}
                else if(start[1]=="/v1/shutdown"){Reply(stream,200,new{ok=true},origin);ThreadPool.QueueUserWorkItem(delegate{Dispose();});}
                else Reply(stream,404,new{error="Unknown MiniAI endpoint."},origin);
            }catch(Exception error){try{Reply(stream,400,new{error=error.Message},origin);}catch(IOException){}catch(ObjectDisposedException){}}
            finally{if(entered){lastRequest=DateTime.UtcNow;Interlocked.Decrement(ref active);slots.Release();}}
        }
        public void Dispose(){lock(stopGate){if(stopped)return;stopped=true;listener.Stop();if(idle!=null)idle.Dispose();translation.Dispose();lock(ocrGate){if(ocr!=null){ocr.Dispose();ocr=null;}if(fastOcr!=null){fastOcr.Dispose();fastOcr=null;}if(formula!=null){formula.Dispose();formula=null;}}ended.Set();}}
    }
}
