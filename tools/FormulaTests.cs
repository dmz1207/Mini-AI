using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;
using MiniAI.Desktop;

namespace MiniAI {
    internal static class FormulaTests {
        static string log;static int count;
        static void Check(bool value,string name){if(!value)throw new Exception(name);count++;File.AppendAllText(log,"PASS: "+name+Environment.NewLine);}
        [STAThread]static int Main(string[] args){log=Path.Combine(args[0],"formula-checks.txt");Directory.CreateDirectory(args[0]);File.WriteAllText(log,"");try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string[] examples={@"h(t)=2\left(e^{-t}-e^{-2t}\right)\varepsilon(t)",@"\frac{x^2+1}{\sqrt[3]{y}}",@"\begin{bmatrix}a&b\\c&d\end{bmatrix}",@"\sum_{i=1}^{n}x_i",@"\int_0^1 f(x)\,dx",@"\hat{\theta}+\mathbf{x}",@"\begin{cases}x&x>0\\0&x\leq0\end{cases}"};
            XNamespace ns="http://www.w3.org/1998/Math/MathML";foreach(string tex in examples){var xml=XElement.Parse(FormulaMarkup.Convert(tex));Check(xml.Name==ns+"math"&&xml.Descendants(ns+"annotation").Single().Value==tex,"well-formed editable MathML retains the original TeX: "+tex);}
            var scripted=XElement.Parse(FormulaMarkup.Convert(@"\sum_{i=1}^{n}x_i"));Check(scripted.Descendants(ns+"msubsup").Count()==1&&scripted.Descendants(ns+"msub").Count()==1,"subscript and superscript retain their separate bases");
            bool rejected=false;try{FormulaMarkup.Convert(@"\unknown{x}");}catch(NotSupportedException){rejected=true;}Check(rejected,"unsupported commands do not silently export a different formula");
            rejected=false;try{FormulaMarkup.Convert(new string('{',70)+"x"+new string('}',70));}catch(FormatException){rejected=true;}Check(rejected,"malformed or excessively nested formulas are bounded");
            Check(!FormulaOcr.IsCandidate("The closed-loop control system remains stable.")&&!FormulaOcr.IsCandidate("event-triggered control protocol"),"hyphenated ordinary prose is not classified as a formula");Check(FormulaOcr.IsCandidate("x2 + y2 = z2"),"equation operators remain eligible for formula recognition");
            string[] source={"three-\naxis spacecraft","com-\nbined control","distur-\nbances","un-\ncertainties","finite-\ntime","event-\ntriggered","alpha-\nunknownword"};string[] expected={"three-axis spacecraft","combined control","disturbances","uncertainties","finite-time","event-triggered","alpha-unknownword"};for(int i=0;i<source.Length;i++)Check(TranslationText.Normalize(source[i])==expected[i],"PDF line-end hyphen: "+expected[i]);
            Check(TranslationText.Normalize("custom-\nword", "customword appears elsewhere")=="customword","document evidence resolves an unfamiliar joined word");
            string paragraph=String.Join(" ",Enumerable.Repeat("The system remains stable.",180));var chunks=TranslationText.Chunks(paragraph,1500);Check(chunks.Count>1&&chunks.Take(chunks.Count-1).All(x=>x.After==" ")&&String.Join("",chunks.Select(x=>x.Text+x.After)).Trim()==paragraph,"long paragraphs retain all text without artificial blank lines");
            using(var preview=FormulaPreview.Render(examples[0])){Check(preview.Width>100&&preview.Height>30,"offline preview renders the user-style equation without an external browser");preview.Save(Path.Combine(args[0],"formula-preview.png"));}
            FormulaMarkup.CopyMathML(examples[0]);var clipboard=Clipboard.GetDataObject();Check(clipboard.GetDataPresent("MathML")&&XElement.Parse(Clipboard.GetText()).Descendants(ns+"msup").Count()==2,"clipboard supplies MathML plus Unicode XML for MathType");
            using(var screenshot=new Bitmap(800,150))using(var window=new OcrResultWindow(screenshot,args[0],delegate(string value){return value;})){window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-25000,-25000);window.Show();Application.DoEvents();window.SetResult("Paragraph followed by formulas",new List<string>{examples[0],examples[1]});Application.DoEvents();var code=(TextBox)window.Controls.Find("ScreenshotTex",true)[0];var contents=(ComboBox)window.Controls.Find("OcrContents",true)[0];Check(code.Text==examples[0]&&contents.Items.Count==3,"each recognized formula can be selected independently from ordinary OCR text");code.Text="x^2";contents.SelectedIndex=2;contents.SelectedIndex=1;Check(code.Text=="x^2","switching formulas preserves manual corrections");code.Select(0,0);window.CopyCode();Check(Clipboard.GetText().Contains("msup"),"editing the code changes the copied MathML");window.ClientSize=new Size(640,480);Application.DoEvents();Check(window.Controls.Find("CopyFormula",true)[0].Visible,"primary copy action remains visible in a compact window");using(var view=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(view,new Rectangle(Point.Empty,view.Size));view.Save(Path.Combine(args[0],"formula-workspace.png"));}window.Close();}
            using(var screenshot=new Bitmap(320,100))using(var window=new OcrResultWindow(screenshot,args[0],delegate(string value){return value;})){window.ShowInTaskbar=false;window.Show();window.ReviewFormula(true);var code=(TextBox)window.Controls.Find("ScreenshotTex",true)[0];Check(code.Enabled&&code.TextLength==0,"unrecognized formulas retain the original and allow a manual TeX draft");code.Text=@"\mathcal{L}V_{i,1}";Application.DoEvents();System.Threading.Thread.Sleep(220);Application.DoEvents();var format=(ComboBox)window.Controls.Find("FormulaCopyFormat",true)[0];Check(code.Text.Contains("V")&&format.SelectedIndex==0,"manual correction remains editable in MathML mode");window.CopyCode();string copied=Clipboard.GetText();File.AppendAllText(log,"Clipboard length: "+copied.Length+"; source="+code.Text+Environment.NewLine);var xml=XElement.Parse(copied);Check(xml.Descendants(ns+"msub").Any()&&xml.Descendants(ns+"mi").Any(x=>x.Value=="V"),"manual formula correction preserves V and its subscript in MathType markup");window.Close();}
            File.AppendAllText(log,"Assertions: "+count);return 0;
        }catch(Exception error){File.AppendAllText(log,error.ToString());return 1;}}
    }
}
