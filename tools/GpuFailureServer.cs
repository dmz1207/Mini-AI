// Test double only. It never loads a model or executes GPU code.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
internal static class GpuFailureServer {
    static int Main(string[] args){
        string log=Environment.GetEnvironmentVariable("MINIAI_TEST_GPU_LOG");if(!String.IsNullOrEmpty(log))File.AppendAllText(log,String.Join(" ",args).Contains("--list-devices")?"list\n":"start\n");
        if(Array.IndexOf(args,"--list-devices")>=0){Console.WriteLine("Vulkan9: NVIDIA simulated test device (8192 MiB)");return 0;}
        int at=Array.IndexOf(args,"--port"),port=Int32.Parse(args[at+1]);var listener=new TcpListener(IPAddress.Loopback,port);listener.Start();
        while(true)using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
            var request=new StringBuilder();int c,previous=0;while((c=stream.ReadByte())>=0){request.Append((char)c);if(request.Length>=4&&request.ToString(request.Length-4,4)=="\r\n\r\n")break;if(++previous>32768)break;}
            bool health=request.ToString().StartsWith("GET /health ");string body=health?"{\"status\":\"ok\"}":"{\"error\":\"simulated GPU allocation failure\"}";
            byte[] bytes=Encoding.UTF8.GetBytes(body),head=Encoding.ASCII.GetBytes("HTTP/1.1 "+(health?"200 OK":"500 Error")+"\r\nContent-Type: application/json\r\nContent-Length: "+bytes.Length+"\r\nConnection: close\r\n\r\n");stream.Write(head,0,head.Length);stream.Write(bytes,0,bytes.Length);
        }
    }
}
