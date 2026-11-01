using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MiniAI.Portable {
    internal static class PortableCleanup {
        static string Hex(byte[] value){return BitConverter.ToString(value).Replace("-","").ToLowerInvariant();}
        static string Name(string prefix,string root){using(var hash=SHA256.Create())return "Local\\"+prefix+Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant()))).ToUpperInvariant();}
        internal static string PackageName(string root){return Name("MiniAI-Package-",root);}
        static bool Locked(string root,string relative) {
            string folder=Launcher.Target(root,relative);if(!Directory.Exists(folder))return false;
            foreach(string path in Directory.GetFiles(folder,"*.lock"))try{using(var lease=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }catch(FileNotFoundException){}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}return false;
        }
        static void Delete(string path){for(int attempt=0;attempt<20;attempt++)try{if(File.Exists(path))File.Delete(path);return;}catch(IOException){Thread.Sleep(100);}catch(UnauthorizedAccessException){return;}}
        internal static void Run(string root) {
            try {
                if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)return;
                using(var package=new Mutex(false,PackageName(root))) {
                    bool owns=false;try{owns=package.WaitOne(30000);}catch(AbandonedMutexException){owns=true;}if(!owns)return;
                    try {
                        if(Locked(root,"state/launchers")||Locked(root,"state/clients"))return;
                        using(var service=new Mutex(false,Name("MiniAI-",root))) {
                            bool idle=false;try{idle=service.WaitOne(0);}catch(AbandonedMutexException){idle=true;}if(!idle)return;
                            try{RemovePayload(root);}finally{service.ReleaseMutex();}
                        }
                    }finally{package.ReleaseMutex();}
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        static void RemovePayload(string root) {
            string text;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniAI.Manifest"))using(var reader=new StreamReader(stream,Encoding.UTF8))text=reader.ReadToEnd();
            var files=new JavaScriptSerializer{MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,Dictionary<string,object>>>(text);
            var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> parents=delegate(string path){for(string folder=Path.GetDirectoryName(path);folder!=null&&folder.StartsWith(root,StringComparison.OrdinalIgnoreCase);folder=Path.GetDirectoryName(folder)){directories.Add(folder);if(String.Equals(folder,root,StringComparison.OrdinalIgnoreCase))break;}};
            foreach(var item in files) {
                string path=Launcher.Target(root,item.Key);parents(path);if(!File.Exists(path))continue;
                // A user-replaced file is preserved; only the embedded payload is owned.
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var hash=SHA256.Create())if(input.Length!=Convert.ToInt64(item.Value["bytes"])||Hex(hash.ComputeHash(input))!=Convert.ToString(item.Value["sha256"]))continue;
                Delete(path);
            }
            foreach(string relative in new[]{"state/connection.json","state/start.lock","last-error.txt","package-checksums.json"}){string path=Launcher.Target(root,relative);parents(path);Delete(path);}
            foreach(string relative in new[]{"state/clients","state/launchers"}) {
                string folder=Launcher.Target(root,relative);directories.Add(folder);parents(folder);
                if(Directory.Exists(folder))foreach(string file in Directory.GetFiles(folder,"*.lock"))try{using(var check=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}Delete(file);}catch(IOException){}
            }
            string temp=Launcher.Target(root,"state/temp");
            if(Directory.Exists(temp)&&File.Exists(Path.Combine(temp,".MiniAI-temp"))) {
                var pending=new Stack<string>();pending.Push(temp);
                while(pending.Count>0){string folder=pending.Pop();directories.Add(folder);foreach(string file in Directory.GetFiles(folder)){Launcher.Target(root,file.Substring(root.TrimEnd('\\').Length+1).Replace('\\','/'));Delete(file);}foreach(string child in Directory.GetDirectories(folder))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);}
            }
            directories.Add(root);
            foreach(string folder in directories.OrderByDescending(path=>path.Length))try{if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)==0&&Directory.GetFileSystemEntries(folder).Length==0)Directory.Delete(folder,false);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
