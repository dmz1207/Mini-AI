using System;
using System.IO;
using System.Text;

namespace MiniAI {
    internal static class ResultStore {
        internal static string Folder(string root){string selected=Environment.GetEnvironmentVariable("MINIAI_RESULTS_DIR");return Path.GetFullPath(String.IsNullOrEmpty(selected)?Path.Combine(Directory.GetParent(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)).FullName,"MiniAI Results"):selected);}
        internal static string Save(string root,string kind,string source,string target,string text) {
            Guard.Check(kind=="ocr"||kind=="translation","Unsupported result type.");Guard.Check(!String.IsNullOrWhiteSpace(text)&&text.Length<=300000&&(source??"").Length<=100000&&(target??"").Length<=64,"Result exceeds the text limit.");
            string directory=Folder(root);Directory.CreateDirectory(directory);string name=(kind=="ocr"?"OCR_":"Translation_")+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,8)+".txt";
            string value=kind=="ocr"?text:"Source / 原文"+Environment.NewLine+(source??"")+Environment.NewLine+Environment.NewLine+"Translation / 译文 ("+target+")"+Environment.NewLine+text;
            string path=Path.Combine(directory,name);using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))using(var writer=new StreamWriter(stream,new UTF8Encoding(true)))writer.Write(value);return path;
        }
    }
}
