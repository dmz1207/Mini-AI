using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MiniAI.Desktop;

namespace MiniAI {
    internal static class BubbleTests {
        static int count;static string log;
        static void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText(log,"PASS: "+message+Environment.NewLine);count++;}
        static void Pump(int milliseconds){var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}}
        static void Away(TranslationBubble bubble){bubble.Show();Application.DoEvents();bubble.Location=new Point(-25000,-25000);typeof(TranslationBubble).GetField("anchor",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bubble,Rectangle.Empty);}
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr a,IntPtr b);
        static int Hit(TranslationBubble bubble,Point point){Point screen=bubble.PointToScreen(point);return SendMessage(bubble.Handle,0x84,IntPtr.Zero,new IntPtr((screen.X&65535)|((screen.Y&65535)<<16))).ToInt32();}
        [STAThread]static int Main(string[] args) {
            log=args[0];File.WriteAllText(log,"");try {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                var api=new BubbleBackend{Text=delegate(string value){return value;},Stream=delegate(string text,string target,string mode,Func<bool> canceled,Action<string> progress){progress("First words");Thread.Sleep(1800);return "Complete translation";}};
                using(var bubble=new TranslationBubble(api,Rectangle.Empty,false)) {
                    Away(bubble);bubble.Translate("Selected text");Pump(1450);Check(!bubble.IsDisposed&&bubble.BusyForTest,"pending translation remains visible with the pointer outside the bubble");
                    int w=bubble.ClientSize.Width,h=bubble.ClientSize.Height;Check(Hit(bubble,new Point(2,h/2))==10&&Hit(bubble,new Point(w-2,h/2))==11&&Hit(bubble,new Point(w/2,h-2))==15&&Hit(bubble,new Point(w/2,2))==12,"left, right, bottom and top borders support native resizing");
                    Check(Hit(bubble,new Point(2,2))==13&&Hit(bubble,new Point(w-2,2))==14&&Hit(bubble,new Point(2,h-2))==16&&Hit(bubble,new Point(w-2,h-2))==17,"all four corners support native resizing on negative monitor coordinates");
                    Check(bubble.Controls[0].Left>=7&&bubble.Controls[0].Right<=w-7,"content leaves a hittable resize border on both sides");
                    Check(bubble.Controls.Find("TranslationHeader",true).Length==1&&bubble.Controls.Find("TranslationDragHandle",true).Single().Cursor==Cursors.SizeAll,"top header and drag handle expose a movable bubble");
                    Check(bubble.ResultForTest=="First words","streamed text appears before inference completes");Pump(2200);Check(!bubble.IsDisposed&&!bubble.BusyForTest&&bubble.ResultForTest=="Complete translation","completed translation stays readable beyond the old 900 ms timeout");
                    bubble.PinForTest();typeof(TranslationBubble).GetField("readableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bubble,DateTime.UtcNow.AddSeconds(-1));Pump(1500);Check(!bubble.IsDisposed,"pinning keeps a finished result visible outside the anchor");bubble.Close();
                }
                string selected="";api.Stream=delegate(string text,string target,string mode,Func<bool> canceled,Action<string> progress){selected=text;if(text=="slow"){Thread.Sleep(1300);progress("stale");}return text=="slow"?"stale":"fresh: "+text;};
                using(var bubble=new TranslationBubble(api,Rectangle.Empty,false)) {
                    Away(bubble);bubble.PinForTest();bubble.Translate("slow");Pump(150);bubble.Translate("chosen words");Pump(1700);Check(bubble.ResultForTest=="fresh: chosen words","late callbacks from canceled selections cannot replace the current result");
                    var source=(TextBox)bubble.Controls.Find("RecognizedSelection",true)[0];var toggle=(Button)bubble.Controls.Find("ToggleTranslationSource",true)[0];IntPtr handle=source.Handle;for(int i=0;i<20;i++){toggle.PerformClick();Application.DoEvents();}Check(source.Handle==handle,"toggling the source repeatedly keeps its native window handle");toggle.PerformClick();source.Text="only these words should translate";source.Select(5,11);typeof(TextBox).GetMethod("OnMouseUp",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,new object[]{new MouseEventArgs(MouseButtons.Left,1,5,5,0)});Pump(200);Check(selected=="these words"&&bubble.ResultForTest=="fresh: these words","source selection translates precisely the selected substring");bubble.ClientSize=new Size(650,400);Pump(50);var close=bubble.Controls.Find("CloseTranslation",true)[0];Check(close.Visible&&close.Parent.ClientRectangle.Contains(close.Bounds),"a resized translation keeps its close button visible");Check(bubble.Opacity==1&&bubble.IsPinned,"pinned translation remains fully opaque");bubble.Close();
                }
                using(var bubble=new TranslationBubble(api,Rectangle.Empty,false)) {
                    Away(bubble);bubble.Translate("short");Pump(250);Check(!bubble.IsDisposed,"successful short result starts its reading grace period");
                    typeof(TranslationBubble).GetField("readableUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bubble,DateTime.UtcNow.AddSeconds(-1));Pump(1500);Check(bubble.IsDisposed,"unpinned result dismisses after its reading grace and mouse leave delay");
                }
                File.AppendAllText(log,"Assertions: "+count);return 0;
            }catch(Exception error){File.AppendAllText(log,error.ToString()+Environment.NewLine+"Native: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());return 1;}
        }
    }
}
