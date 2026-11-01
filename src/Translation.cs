using System;
using System.Collections.Generic;
using System.Threading;

namespace MiniAI {
    // The offline application ships Hy-MT2 only. C# owns the local STQ
    // llama.cpp process and streams/caches its translation responses.
    internal sealed class Translation : IDisposable {
        internal static readonly Dictionary<string,string> Languages=MiniAI.Desktop.LanguageCatalog.Names;
        readonly HyTranslation hy;
        volatile bool disposed;

        internal Translation(string directory){hy=new HyTranslation(directory);}
        internal bool HyAvailable {get{return hy.Available;}}
        internal bool Available {get{return hy.Available;}}
        internal string DefaultEngine {get{return "hy";}}
        internal bool EngineAvailable(string engine){return engine=="hy"&&hy.Available;}
        internal object Status {get{return hy.Status;}}
        internal void Prewarm(){if(!HyAvailable)return;ThreadPool.QueueUserWorkItem(delegate{if(disposed)return;hy.Prewarm(delegate{return disposed;});});}
        internal object Translate(string text,string target,Func<bool> canceled,Action<string> progress=null,string mode="fast",string engine=null){
            Guard.Check(!disposed,"MiniAI translation has stopped.");
            Guard.Check(String.IsNullOrEmpty(engine)||engine=="hy","Hy-MT2 is the only bundled translation engine.");
            Guard.Check(mode=="fast"||mode=="quality","Unsupported translation mode.");
            Guard.Check(Languages.ContainsKey(target),"Unsupported translation target.");
            Guard.Check(hy.Available,"The complete Hy-MT2 offline package is missing.");
            return hy.Translate(text,target,canceled,progress,mode);
        }
        internal void ReleaseIfIdle(){hy.ReleaseIfIdle();}
        public void Dispose(){disposed=true;hy.Dispose();}
    }
}
