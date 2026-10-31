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
