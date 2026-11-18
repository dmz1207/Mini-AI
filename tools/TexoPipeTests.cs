using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Exercise the production TexoWorker transport with a controllable worker.
// No model inference is mocked in the separate formula endpoint/portable tests.
internal static class TexoPipeTests {
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    static Type workerType;
    static MethodInfo recognize;
    static string root;
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS: "+name);}
    static object Worker(){return Activator.CreateInstance(workerType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{root},null);}
    static string Read(object worker,byte[] bytes,int width,int height,Func<bool> canceled=null){
        try{return (string)recognize.Invoke(worker,new object[]{bytes,width,height,0f,canceled});}
        catch(TargetInvocationException error){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Thread.Sleep(100);}
    static void Cancel(string name,int width,int height,bool connect){
        object worker=Worker();var clock=Stopwatch.StartNew();
        if(connect)Environment.SetEnvironmentVariable("MINIAI_PIPE_TEST_DELAY_CONNECT","1");
        try{
            var bytes=new byte[width*height];bytes[0]=1;
            bool canceled=false;try{Read(worker,bytes,width,height,delegate{return clock.ElapsedMilliseconds>=150;});}catch(OperationCanceledException){canceled=true;}
            Check(canceled&&clock.ElapsedMilliseconds<2500,name+" cancels promptly");
        }finally{Environment.SetEnvironmentVariable("MINIAI_PIPE_TEST_DELAY_CONNECT",null);((IDisposable)worker).Dispose();}
        // Let canceled native I/O callbacks finish before a new connection.
        Thread.Sleep(100);Collect();
        worker=Worker();try{Check(Read(worker,new byte[64],8,8)=="x+y",name+" allows a fresh worker");}finally{((IDisposable)worker).Dispose();}
    }
    static int Fake(string name){
        if(Environment.GetEnvironmentVariable("MINIAI_PIPE_TEST_DELAY_CONNECT")=="1")Thread.Sleep(5000);
        try{using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut)){
            pipe.Connect(10000);using(var reader=new BinaryReader(pipe,Encoding.UTF8,true)){
                while(true){int width;try{width=reader.ReadInt32();}catch(EndOfStreamException){return 0;}
                    int height=reader.ReadInt32(),length=reader.ReadInt32();if(length<1||length>2000000||width*height!=length)return 3;
                    if(width==1024)Thread.Sleep(5000); // Backpressure while the server writes.
                    byte[] gray=reader.ReadBytes(length);if(gray.Length!=length)return 0;
                    if(gray[0]==1)Thread.Sleep(5000); // Pending server read.
                    if(gray[0]==2)return 0; // Unexpected worker exit.
                    byte[] body=Encoding.UTF8.GetBytes("{\"tex\":\"x+y\",\"confidence\":0.9,\"cpu\":\"test\",\"error\":\"\"}");
                    byte[] header=BitConverter.GetBytes(body.Length);
                    pipe.Write(header,0,1);pipe.Flush();Thread.Sleep(1);pipe.Write(header,1,3);
                    pipe.Write(body,0,2);pipe.Flush();Thread.Sleep(1);pipe.Write(body,2,body.Length-2);pipe.Flush();
                }
            }
        }}catch(IOException){return 0;}
    }
    static int Main(string[] args){
        SetErrorMode(0x0002|0x8000);
        if(args.Length==2&&args[0]=="--formula-worker")return Fake(args[1]);
        try{
            string assembly=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);root=Path.Combine(output,"pipe sandbox 空格");Directory.CreateDirectory(root);
            File.Copy(Assembly.GetExecutingAssembly().Location,Path.Combine(root,"MiniAI.exe"),true);
            workerType=Assembly.LoadFile(assembly).GetType("MiniAI.TexoWorker",true);recognize=workerType.GetMethod("Recognize",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int n=0;n<3;n++){Cancel("connect",8,8,true);Cancel("read",8,8,false);Cancel("write",1024,1024,false);}
            object worker=Worker();try{
                for(int i=0;i<30;i++)Read(worker,new byte[64],8,8);
                Collect();int before=Process.GetCurrentProcess().HandleCount;
                for(int i=0;i<1000;i++)if(Read(worker,new byte[64],8,8)!="x+y")throw new Exception("Corrupt fragmented frame");
                Check(true,"1000 fragmented request/reply exchanges preserve data");
                Collect();int after=Process.GetCurrentProcess().HandleCount;
                Check(after-before<32,"repeated exchanges do not leak wait handles (growth "+(after-before)+")");
                var broken=new byte[64];broken[0]=2;bool failed=false;try{Read(worker,broken,8,8);}catch(IOException){failed=true;}
                Check(failed,"worker exit returns a recoverable I/O error");
                Check(Read(worker,new byte[64],8,8)=="x+y","recognition restarts after worker exit");
            }finally{((IDisposable)worker).Dispose();}
            Collect();
            string record=new JavaScriptSerializer().Serialize(new{assertions=checks.Count,exchanges=1030,cancellation_cycles=9,checks=checks});File.WriteAllText(Path.Combine(output,"texo-pipe-checks.json"),record,new UTF8Encoding(false));Console.WriteLine(record);return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
