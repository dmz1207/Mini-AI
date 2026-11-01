using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MiniAI.Desktop;

namespace MiniAI {
    internal static class ImageTests {
        static string log;static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;File.AppendAllText(log,"PASS: "+message+Environment.NewLine);}
        static void Pump(Func<bool> done){var clock=System.Diagnostics.Stopwatch.StartNew();while(!done()&&clock.ElapsedMilliseconds<90000){Application.DoEvents();Thread.Sleep(10);}Application.DoEvents();}
        static int LightPixels(Bitmap image,Rectangle area){int count=0;for(int y=area.Top;y<area.Bottom;y+=2)for(int x=area.Left;x<area.Right;x+=2)if(image.GetPixel(x,y).GetBrightness()>.65)count++;return count;}
        [STAThread]static int Main(string[] args) {
            string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);log=Path.Combine(output,"image-checks.txt");File.WriteAllText(log,"");
            try {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var state=Json.Read(File.ReadAllText(Path.Combine(root,"state/connection.json")));int calls=0;
                Func<string,string,string,Func<bool>,Action<string>,string> translate=delegate(string value,string target,string mode,Func<bool> canceled,Action<string> progress){return TranslationStream.Read(Json.Get(state,"url"),Json.Get(state,"token"),value,target,canceled,progress,mode);};
                var bubble=new BubbleBackend{Text=delegate(string value){return value;},Stream=translate};
                var services=new ImageServices{Bubble=bubble,Text=(Func<string,string>)Delegate.CreateDelegate(typeof(Func<string,string>),typeof(Workbench).GetMethod("English",BindingFlags.Static|BindingFlags.NonPublic)),Translate=translate,Ocr=delegate(byte[] bytes,string mode,Func<bool> canceled){calls++;var data=Json.Request(Json.Get(state,"url")+"/v1/ocr",new{image_base64=Convert.ToBase64String(bytes),mode=mode},Json.Get(state,"token"),90,canceled);return (List<ImageRegion>)typeof(Workbench).GetMethod("ImageLines",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{data});}};
                using(var fixture=new Bitmap(1400,700)) {
                    using(var graphics=Graphics.FromImage(fixture))using(var font=new Font("Arial",38)){graphics.Clear(Color.FromArgb(37,38,39));graphics.DrawString("6. The control system remains stable.",font,Brushes.White,65,70);graphics.DrawString("The simulation results verify the method.",font,Brushes.White,110,140);graphics.DrawString("7. Protect your attention and recovery.",font,Brushes.White,65,330);}
                    fixture.Save(Path.Combine(output,"dark-source.png"),ImageFormat.Png);
                    string results=Path.Combine(output,"results-"+Guid.NewGuid().ToString("N"));using(var window=new ImageTranslationWindow(services,new Bitmap(fixture),results,true)) {
                        window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-25000,-25000);window.Show();var started=System.Diagnostics.Stopwatch.StartNew();window.ExtractText();Pump(delegate{return !window.BusyForTest;});Check(window.TextForTest.ToLowerInvariant().Contains("control system"),"dark-background image OCR exposes selectable text");File.AppendAllText(log,"OCR_SECONDS: "+started.Elapsed.TotalSeconds.ToString("F3")+Environment.NewLine);
                        int before=calls;started.Restart();window.TranslateImage();Pump(delegate{return !window.BusyForTest;});Check(window.ImageForTest!=null,"translation produces a rendered image through the actual offline service");Check(calls==before,"image translation reuses extracted OCR geometry");
                        Check(window.ImageForTest.Width==fixture.Width&&window.ImageForTest.GetPixel(0,0).ToArgb()==fixture.GetPixel(0,0).ToArgb(),"image width and background outside text remain unchanged");
                        Check(LightPixels(window.ImageForTest,new Rectangle(70,330,1000,62))>100,"single-line translated blocks remain visibly rendered rather than clipped away");
                        Check(window.TextForTest.StartsWith("6. ")&&!window.TextForTest.Contains("国家"),"numbered image paragraphs preserve their list markers without changing the statement meaning");
                        window.ExtractText();Check(window.TextForTest.Contains("稳定"),"text extraction from the translated view exposes its Chinese text without another OCR pass");Check(calls==before,"translated text extraction does not repeat OCR");
                        Check(Directory.GetFiles(results,"*.png").Length==1&&Directory.GetFiles(results,"*.txt").Length==2,"regular OCR and image translation persist only text/image results");File.AppendAllText(log,"IMAGE_TRANSLATION_SECONDS: "+started.Elapsed.TotalSeconds.ToString("F3")+Environment.NewLine);
                        window.ImageForTest.Save(Path.Combine(output,"dark-translated.png"),ImageFormat.Png);using(var snapshot=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(snapshot,new Rectangle(Point.Empty,window.Size));snapshot.Save(Path.Combine(output,"image-window.png"));}
                        var field=typeof(ImageTranslationWindow).GetField("quality",BindingFlags.Instance|BindingFlags.NonPublic);((ComboBox)field.GetValue(window)).SelectedIndex=1;window.TranslateImage();Pump(delegate{return !window.BusyForTest;});Check(calls==before+1,"switching fast OCR to quality reruns recognition with medium");Check(window.ImageForTest!=null&&window.TextForTest.Contains("稳定"),"high-quality image translation uses the alternate decoding mode");window.Close();
                    }
                    string instant=Path.Combine(output,"instant-must-not-exist");using(var window=new ImageTranslationWindow(services,new Bitmap(fixture),instant,false)) {
                        window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-25000,-25000);window.Show();window.ExtractText();Pump(delegate{return !window.BusyForTest;});Check(!Directory.Exists(instant),"instant text extraction writes no result files");window.Close();
                    }
                }
                File.AppendAllText(log,"Assertions: "+count);return 0;
            }catch(Exception error){File.AppendAllText(log,error.ToString());return 1;}
        }
    }
}
