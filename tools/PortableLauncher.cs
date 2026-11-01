using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MiniAI.Portable {
    internal static class Launcher {
        static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        static string Digest(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var hash=SHA256.Create())return Hex(hash.ComputeHash(stream));}
        static string Target(string root,string relative) {
            if(String.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":")||relative.Contains("\\"))throw new IOException("Invalid package path.");
            string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid package path.");
            for(string parent=Path.GetDirectoryName(path);parent!=null;parent=Path.GetDirectoryName(parent)) {
                if(Directory.Exists(parent)&&(File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a regular folder for the portable package / 请放在普通文件夹中运行。");
                if(String.Equals(parent,root,StringComparison.OrdinalIgnoreCase))break;
            }
            if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Invalid package destination.");
            return path;
        }
        internal static void Prepare(string root,Action<int,string> progress) {
            string manifest;
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniAI.Manifest")) {
                if(resource==null||resource.Length>1024*1024)throw new IOException("Missing offline package / 离线包不完整。");
                using(var reader=new StreamReader(resource,Encoding.UTF8))manifest=reader.ReadToEnd();
            }
            var json=new JavaScriptSerializer{MaxJsonLength=1024*1024};var files=json.Deserialize<Dictionary<string,Dictionary<string,object>>>(manifest);
            if(files==null||files.Count<4||files.Count>1000)throw new IOException("Invalid package manifest.");
            long total=0;foreach(var file in files){long size=Convert.ToInt64(file.Value["bytes"]);if(size<0||size>1024L*1024*1024)throw new IOException("Invalid package size.");total+=size;Target(root,file.Key);}
            if(total>2L*1024*1024*1024)throw new IOException("Invalid package size.");
            Directory.CreateDirectory(root);long done=0;
            using(var source=new FileStream(Assembly.GetExecutingAssembly().Location,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var archive=new ZipArchive(source,ZipArchiveMode.Read)) {
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var entry in archive.Entries)if(!names.Add(entry.FullName))throw new IOException("Duplicate package entry.");
                foreach(var file in files) {
                    string path=Target(root,file.Key),expected=Convert.ToString(file.Value["sha256"]);long size=Convert.ToInt64(file.Value["bytes"]);
                    var entry=archive.GetEntry("MiniAI/"+file.Key);if(entry==null||entry.Length!=size)throw new IOException("Incomplete offline package: "+file.Key);
                    progress((int)(done*100/Math.Max(1,total)),"Checking / 正在检查 "+Path.GetFileName(path));
                    if(File.Exists(path)&&new FileInfo(path).Length==size&&Digest(path)==expected){done+=size;continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));string pending=path+".miniai-"+Guid.NewGuid().ToString("N")+".part";
                    try {
                        using(var input=entry.Open())using(var output=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var hash=SHA256.Create()) {
                            var buffer=new byte[256*1024];int read;long written=0,last=0;var watch=Stopwatch.StartNew();
                            while((read=input.Read(buffer,0,buffer.Length))>0){written+=read;if(written>size)throw new IOException("Invalid package entry size.");output.Write(buffer,0,read);hash.TransformBlock(buffer,0,read,buffer,0);if(watch.ElapsedMilliseconds-last>80){last=watch.ElapsedMilliseconds;progress((int)((done+written)*100/Math.Max(1,total)),"Preparing / 正在准备 "+Path.GetFileName(path));}}
                            hash.TransformFinalBlock(new byte[0],0,0);if(written!=size||Hex(hash.Hash)!=expected)throw new IOException("Package checksum mismatch: "+file.Key+" / 请重新下载完整文件。");
                        }
                        if(File.Exists(path))File.Replace(pending,path,null);else File.Move(pending,path);
                    }finally{if(File.Exists(pending))File.Delete(pending);}
                    done+=size;
                }
            }
            File.WriteAllText(Path.Combine(root,"package-checksums.json"),manifest,new UTF8Encoding(false));progress(100,"Ready / 准备完成");
        }
        static string Quote(string value){var text=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){text.Append('\\',slashes*2+1).Append(c);slashes=0;continue;}text.Append('\\',slashes).Append(c);slashes=0;}return text.Append('\\',slashes*2).Append('"').ToString();}
        [STAThread]static int Main(string[] args) {
            bool headless=Array.IndexOf(args,"--headless")>=0,extractOnly=Array.IndexOf(args,"--extract-only")>=0;
            string home=AppDomain.CurrentDomain.BaseDirectory,root=Path.Combine(home,"MiniAI");
            try {
                string name;using(var hash=SHA256.Create())name="Local\\MiniAI-Package-"+Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
                using(var mutex=new Mutex(false,name)) {
                    bool owns=false;try{owns=mutex.WaitOne(120000);}catch(AbandonedMutexException){owns=true;}
                    if(!owns)throw new IOException("Another MiniAI package is still preparing / 另一个功能包正在准备，请稍后重试。");
                    try {
                        if(headless||extractOnly)Prepare(root,delegate{});
                        else {Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var window=new Preparation(root)){Application.Run(window);if(window.Error!=null)throw window.Error;}}
                    }finally{mutex.ReleaseMutex();}
                }
                if(extractOnly)return 0;
                var arguments=new StringBuilder();foreach(string arg in args){if(arguments.Length>0)arguments.Append(' ');arguments.Append(Quote(arg));}
                var start=new ProcessStartInfo(Path.Combine(root,"MiniAI.exe"),arguments.ToString()){UseShellExecute=false,WorkingDirectory=root,CreateNoWindow=true};
                start.EnvironmentVariables["TEMP"]=root;start.EnvironmentVariables["TMP"]=root;
                using(var child=Process.Start(start)){child.WaitForExit();return child.ExitCode;}
            }catch(Exception error){string message=error.Message+"\n\nKeep the EXE in a writable folder. Close MiniAI before replacing an older package. / 请放在可写入的文件夹中，更新前关闭旧版 MiniAI。";if(headless||extractOnly){try{File.WriteAllText(Path.Combine(home,"MiniAI-portable-error.txt"),message);}catch(IOException){}catch(UnauthorizedAccessException){}}else MessageBox.Show(message,"MiniAI",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        sealed class Preparation:Form {
            readonly Label text;readonly ProgressBar bar;internal Exception Error;
            internal Preparation(string root) {
                Text="MiniAI";Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);ClientSize=new Size(520,155);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ControlBox=false;StartPosition=FormStartPosition.CenterScreen;
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
                text=new Label{AutoSize=true,Dock=DockStyle.Fill,Text="Preparing offline OCR and translation / 准备离线 OCR 与翻译",Margin=new Padding(0,0,0,14)};layout.Controls.Add(text);bar=new ProgressBar{Dock=DockStyle.Fill,Height=22,Margin=new Padding(0,0,0,12)};layout.Controls.Add(bar);layout.Controls.Add(new Label{Text="First run extracts beside this EXE / 首次启动解包到本文件夹",AutoSize=true,Dock=DockStyle.Fill});
                Shown+=delegate{new Thread(delegate(){try{Prepare(root,delegate(int value,string label){BeginInvoke(new Action(delegate{bar.Value=value;text.Text=label;}));});}catch(Exception error){Error=error;}finally{BeginInvoke(new Action(Close));}}){IsBackground=true,Name="MiniAI package preparation"}.Start();};
            }
        }
    }
}
