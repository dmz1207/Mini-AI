using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MiniAI.Desktop;

namespace MiniAI {
    internal static class DesktopTests {
        static int count;
        static string log;
        static void Check(bool value,string name){if(!value)throw new Exception(name);count++;File.AppendAllText(log,"PASS: "+name+Environment.NewLine);}
        static void Pump(Func<bool> done){var watch=System.Diagnostics.Stopwatch.StartNew();while(!done()&&watch.ElapsedMilliseconds<90000){Application.DoEvents();System.Threading.Thread.Sleep(10);}Application.DoEvents();}
        [STAThread]static int Main(string[] args) {
            string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);log=Path.Combine(output,"desktop-checks.txt");File.WriteAllText(log,"");
            try {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var state=Json.Read(File.ReadAllText(Path.Combine(root,"state","connection.json")));
                using(var window=new Workbench(root,Json.Get(state,"url"))) {
                    window.Show();Application.DoEvents();var input=(TextBox)window.Controls.Find("TranslationSource",true)[0];var result=(TextBox)window.Controls.Find("TranslationResult",true)[0];
                    var field=typeof(Workbench).GetField("translate",BindingFlags.Instance|BindingFlags.NonPublic);var button=(Button)field.GetValue(window);input.Text="The control system remains stable.";button.PerformClick();Pump(delegate{return button.Enabled;});Check(result.Text.Contains("稳定"),"standalone workspace translates through the actual local service");
                    input.SelectAll();typeof(TextBox).GetMethod("OnMouseUp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(input,new object[]{new MouseEventArgs(MouseButtons.Left,1,10,10,0)});Application.DoEvents();
                    var popup=(TranslationBubble)typeof(Workbench).GetField("bubble",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);Check(popup!=null,"standalone text selection opens the shared translation bubble");popup.PinForTest();Pump(delegate{return !popup.BusyForTest;});Check(popup.ResultForTest.Contains("稳定"),"standalone selection translation completes");popup.Close();
                    var languages=(ComboBox)typeof(Workbench).GetField("language",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);Check(languages.SelectedIndex==0&&languages.Items.Count==12,"standalone workspace defaults to automatic direction and exposes the installed model targets");Application.DoEvents();window.Refresh();
                    using(var image=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(image,new Rectangle(Point.Empty,window.Size));image.Save(Path.Combine(output,"standalone-window.png"));}
                    string photo=Path.Combine(root,"work","tests","ocr-bilingual.png");
                    if(File.Exists(photo)) {
                        window.LoadPhoto(photo);Pump(delegate{return button.Enabled;});Check(input.Text.Contains("control system")&&input.Text.Contains("中英文翻译"),"photo import fills editable OCR text without forcing translation");
                        using(var image=Image.FromFile(photo))Clipboard.SetImage(image);
                        input.Clear();window.PasteInput();Pump(delegate{return button.Enabled;});Check(input.Text.Contains("control system"),"clipboard image OCR fills editable source text");
                        input.Clear();var data=new DataObject(DataFormats.FileDrop,new[]{photo});typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{new DragEventArgs(data,0,0,0,DragDropEffects.Copy,DragDropEffects.Copy)});Pump(delegate{return button.Enabled;});Check(input.Text.Contains("control system"),"dragged photo uses the same OCR workflow");
                        string oriented=Path.Combine(root,"work","tests","photo-oriented.jpg");if(File.Exists(oriented)){window.LoadPhoto(oriented);Pump(delegate{return button.Enabled;});Check(input.Text.Contains("control system"),"phone JPEG orientation is corrected before OCR");}
                    }
                    using(var large=new Bitmap(6000,500)){byte[] bytes=Workbench.PhotoBytes(large);using(var stream=new MemoryStream(bytes))using(var resized=Image.FromStream(stream))Check(resized.Width==4096&&resized.Height>0,"large photos are bounded before sending to OCR");}
                    string missing=Path.Combine(output,"missing-runtime");Directory.CreateDirectory(missing);foreach(string dependency in new[]{"onnxruntime.dll","msvcp140.dll","vcruntime140.dll","vcruntime140_1.dll"})File.Copy(Path.Combine(root,"runtime",dependency),Path.Combine(missing,dependency),true);bool caught=false;try{NativeDependencies.Check(Path.Combine(missing,"onnxruntime.dll"));}catch(IOException error){caught=error.Message.IndexOf("msvcp140_1.dll",StringComparison.OrdinalIgnoreCase)>=0;}Check(caught,"missing delayed OCR dependency is rejected before entering the Windows loader");
                    using(var image=new Bitmap(320,200)) {
                        using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.White);
                        var constructor=typeof(CaptureOverlay).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(Bitmap),typeof(Rectangle)},null);
                        using(var overlay=(CaptureOverlay)constructor.Invoke(new object[]{image,new Rectangle(0,0,320,200)})) {
                            overlay.Show();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                            typeof(CaptureOverlay).GetMethod("OnMouseDown",flags).Invoke(overlay,new object[]{new MouseEventArgs(MouseButtons.Left,1,260,150,0)});
                            typeof(CaptureOverlay).GetMethod("OnMouseMove",flags).Invoke(overlay,new object[]{new MouseEventArgs(MouseButtons.Left,0,30,20,0)});
                            typeof(CaptureOverlay).GetMethod("OnMouseUp",flags).Invoke(overlay,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,20,0)});
                            Check(overlay.Result!=null&&overlay.Result.Size==new Size(230,130),"real screen-selector mouse events handle reverse-direction dragging");overlay.Result.Dispose();overlay.Close();
                        }
                    }
                }
                File.AppendAllText(log,"Assertions: "+count);return 0;
            }catch(Exception error){File.AppendAllText(log,error.ToString());return 1;}
        }
    }
}
