using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MiniAI {
    internal static class Program {
        [DllImport("kernel32.dll")]static extern uint SetErrorMode(uint mode);
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        [STAThread]static int Main(string[] args) {
            SetErrorMode(2|0x8000);SetProcessDPIAware();
            string root=AppDomain.CurrentDomain.BaseDirectory;bool headless=Array.IndexOf(args,"--headless")>=0;int port=17643;
            try {
                for(int i=0;i+1<args.Length;i++)if(args[i]=="--port")port=Int32.Parse(args[i+1]);
                Guard.Check(port>=0&&port<=65535,"Port must be between 0 and 65535.");
                string name;using(var hash=SHA256.Create())name="Local\\MiniAI-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()))).Replace("-","");
                using(var single=new Mutex(false,name)) {
                    bool owns=false;try{owns=single.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
                    try {
                        if(!owns){if(!headless)ShowExisting(root);return 0;}
                        using(var service=new Service(root,port,headless)) {
                    if(headless){service.Run();return 0;}
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    using(var window=new Workbench(root,service.Address)) {
                        new Thread(delegate(){try{service.Run();}catch(Exception error){try{window.BeginInvoke(new Action(delegate{MessageBox.Show(window,error.Message,"MiniAI");window.Close();}));}catch(InvalidOperationException){}}}){IsBackground=true,Name="MiniAI local HTTP"}.Start();
                        Application.Run(window);
                    }
                        }
                    }finally{if(owns)single.ReleaseMutex();}
                }
                return 0;
            }catch(Exception error){if(headless){File.WriteAllText(Path.Combine(root,"last-error.txt"),error.Message);return 1;}MessageBox.Show(error.Message,"MiniAI",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
        }
        static void ShowExisting(string root) {
            var connection=Json.Read(File.ReadAllText(Path.Combine(root,"state","connection.json"),Encoding.UTF8));string address=Json.Get(connection,"url"),token=Json.Get(connection,"token");Uri uri;
            Guard.Check(Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Scheme=="http"&&uri.Host=="127.0.0.1"&&uri.AbsolutePath=="/"&&uri.UserInfo.Length==0,"Invalid MiniAI connection.");
            Json.Request(address+"/health",null,token,2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var window=new Workbench(root,address))Application.Run(window);
        }
    }
}
