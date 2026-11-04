using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MiniAI {
    internal static class GpuFallbackTests {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateHardLink(string name,string existing,IntPtr security);
        static int count;static string log;
        static void Check(bool value,string text){if(!value)throw new Exception(text);File.AppendAllText(log,"PASS: "+text+Environment.NewLine);count++;}
        static void Link(string source,string target){Directory.CreateDirectory(Path.GetDirectoryName(target));if(!File.Exists(target)&&!CreateHardLink(target,source,IntPtr.Zero))File.Copy(source,target);}
        static int Main(string[] args){string source=args[0],output=args[1],root=Path.Combine(output,"simulated GPU pack"),previous=Environment.GetEnvironmentVariable("MINIAI_DEVICE"),previousLog=Environment.GetEnvironmentVariable("MINIAI_TEST_GPU_LOG");Directory.CreateDirectory(output);log=Path.Combine(output,"gpu-fallback-checks.txt");File.WriteAllText(log,"");try{
            Check(HyTranslation.IntegratedDevice("Vulkan0: Intel UHD Graphics 620"),"automatic mode leaves known weak integrated graphics on CPU");
            Check(HyTranslation.IntegratedDevice("Vulkan0: llvmpipe software renderer"),"automatic mode rejects a software Vulkan renderer");
            Check(!HyTranslation.IntegratedDevice("Vulkan0: NVIDIA GeForce RTX 4070"),"a discrete GPU remains eligible for automatic acceleration");
            Link(Path.Combine(source,"models/translation/hy-mt2.gguf"),Path.Combine(root,"models/translation/hy-mt2.gguf"));
            foreach(string file in Directory.GetFiles(Path.Combine(source,"runtime"),"*.dll"))Link(file,Path.Combine(root,"runtime",Path.GetFileName(file)));
            foreach(string variant in new[]{"avx2","baseline"})foreach(string file in Directory.GetFiles(Path.Combine(source,"runtime/translation",variant)))Link(file,Path.Combine(root,"runtime/translation",variant,Path.GetFileName(file)));
            string fake=Path.Combine(root,"runtime/translation/vulkan/llama-server.exe");Directory.CreateDirectory(Path.GetDirectoryName(fake));File.Copy(Path.Combine(output,"GpuFailureServer.exe"),fake,true);
            string marker=Path.Combine(output,"simulated-device.log");File.WriteAllText(marker,"");Environment.SetEnvironmentVariable("MINIAI_TEST_GPU_LOG",marker);Environment.SetEnvironmentVariable("MINIAI_DEVICE","auto");
            using(var engine=new HyTranslation(root)){
                var reply=(Dictionary<string,object>)engine.Translate("The control system remains stable.","zh",delegate{return false;},null,"fast");
                Check(File.ReadAllText(marker).Contains("list")&&File.ReadAllText(marker).Contains("start"),"automatic device discovery and GPU startup were attempted against the test double");
                Check(Convert.ToString(reply["device"])=="CPU","a GPU inference failure automatically restarts the actual CPU backend");
                Check(Convert.ToString(reply["text"]).Contains("稳定"),"the actual Hy CPU model returns the complete translation after GPU failure");
                Check(!Convert.ToBoolean(reply["truncated"]),"fallback translation is not truncated");
            }
            File.AppendAllText(log,"Assertions: "+count);return 0;
        }catch(Exception error){File.AppendAllText(log,error.ToString());return 1;}finally{Environment.SetEnvironmentVariable("MINIAI_DEVICE",previous);Environment.SetEnvironmentVariable("MINIAI_TEST_GPU_LOG",previousLog);}}
    }
}
