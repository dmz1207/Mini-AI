using System;
using System.Diagnostics;
using System.IO;

namespace MiniAI {
    internal sealed class ClientLease : IDisposable {
        FileStream file;
        internal ClientLease(string root){string folder=Path.Combine(root,"state/clients");Directory.CreateDirectory(folder);file=new FileStream(Path.Combine(folder,Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N")+".lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose);}
        internal static bool Any(string root){string folder=Path.Combine(root,"state/clients");if(!Directory.Exists(folder))return false;foreach(string path in Directory.GetFiles(folder,"*.lock"))try{using(var lease=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }catch(FileNotFoundException){}catch(IOException){return true;}catch(UnauthorizedAccessException){return true;}return false;}
        public void Dispose(){if(file!=null){file.Dispose();file=null;}}
    }
}
