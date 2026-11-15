using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MiniAI {
    // Texo-distill Q8_0 is a dedicated mathematical-expression recognizer.
    // Keep normal OCR, mixed-image routing and editable MathML independent.
    internal sealed class FormulaOcr : IDisposable {
        readonly TexoWorker worker;
        readonly object gate=new object();
        internal string Cpu {get{return worker.Cpu??"";}}
        internal FormulaOcr(string root){Guard.Check(Available(root),"Texo Q8_0 formula package is missing. / 缺少公式识别功能包。");worker=new TexoWorker(root);}
        internal static bool Available(string root){foreach(string file in new[]{"models/formula/texo-distill-q8_0.gguf","runtime/formula/crispembed.dll","runtime/formula/ggml.dll","runtime/formula/ggml-base.dll","runtime/formula/ggml-cpu.dll"})if(!File.Exists(Path.Combine(root,file)))return false;return true;}
        internal static bool IsCandidate(string text) {
            if(String.IsNullOrWhiteSpace(text))return false;int letters=0,digits=0,operators=0;
            foreach(char c in text){if(Char.IsLetter(c))letters++;else if(Char.IsDigit(c))digits++;else if("=+-*/^_{}()[]<>≤≥∑∫√≈≠%".IndexOf(c)>=0)operators++;}
            int proseWords=Regex.Matches(text,@"[A-Za-z]{3,}").Count;
            if(proseWords>=3)return false;
            bool strong=text.IndexOfAny(new[]{'=','+','*','/','^','_','≤','≥','∑','∫','√','≈','≠'})>=0;
            if(!strong&&proseWords>=2)return false;
            if(operators>=1&&digits+letters>=2&&(strong||letters<=6))return true;
            if(text.Trim().Length==1&&letters==1)return true;
            return digits>=2&&letters<=Math.Max(8,digits*2);
        }
        internal static bool IsMathGroup(string text,int count){if(count>1)return true;if(String.IsNullOrWhiteSpace(text))return false;int letters=0,digits=0,operators=0;foreach(char c in text){if(Char.IsLetter(c))letters++;else if(Char.IsDigit(c))digits++;else if("=+-*/^_{}()[]<>≤≥∑∫√≈≠%".IndexOf(c)>=0)operators++;}return operators>0&&letters+digits>0||digits>0&&letters<=4;}
        internal static string Stabilize(string source,string latex) {
            if(String.IsNullOrWhiteSpace(source)||String.IsNullOrWhiteSpace(latex))return latex;
            // Preserve clearly printed operators and variable case when the
            // encoder/decoder makes a confident but visibly contradictory guess.
            string structure=Regex.Replace(latex,@"\\(?:mathbf|mathrm|mathit|mathsf|mathtt|times|cdot|div)\b","");
            if(Regex.IsMatch(structure,@"\\[A-Za-z]+"))return latex;
            if(Regex.IsMatch(latex,@"[_^]")&&!Regex.IsMatch(source,@"[⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼_^]"))return latex;
            if(source.Length>80||source.IndexOfAny(new[]{'\n','\r'})>=0)return latex;
            string simple=Regex.Replace(source,@"²|³|⁰|¹|⁴|⁵|⁶|⁷|⁸|⁹|⁺|⁻|⁼",delegate(Match m){int n="⁰¹²³⁴⁵⁶⁷⁸⁹".IndexOf(m.Value,StringComparison.Ordinal);string digit=n>=0?n.ToString():m.Value=="⁺"?"+":m.Value=="⁻"?"-":"=";return "^{"+digit+"}";});
            simple=simple.Replace('−','-').Replace('×','*').Replace('÷','/').Trim();if(simple.IndexOfAny(new[]{'=','+','-','<','>'})<0)return latex;
            string rawLetters=Regex.Replace(simple,@"[^A-Za-z]","");string texLetters=Regex.Replace(Regex.Replace(latex,@"\\[A-Za-z]+\*?", ""),@"[^A-Za-z]","");string rawDigits=Regex.Replace(simple,@"[^0-9]","");string texDigits=Regex.Replace(Regex.Replace(latex,@"\\[A-Za-z]+\*?", ""),@"[^0-9]","");
            foreach(char op in new[]{'=','+','-','/','<','>'})if(Count(simple,op)!=Count(latex,op))return simple;
            if(rawLetters!=texLetters||rawDigits!=texDigits)return simple;
            return latex;
        }
        static int Count(string text,char value){int total=0;foreach(char c in text)if(c==value)total++;return total;}
        static Rectangle Bounds(PointF[] normalized,int width,int height) {
            float left=width,top=height,right=0,bottom=0;if(normalized!=null)foreach(PointF point in normalized){left=Math.Min(left,point.X*width);right=Math.Max(right,point.X*width);top=Math.Min(top,point.Y*height);bottom=Math.Max(bottom,point.Y*height);}
            if(right<=left||bottom<=top)return Rectangle.Empty;float mx=Math.Max(4,(right-left)*0.08f),my=Math.Max(4,(bottom-top)*0.22f);return Rectangle.FromLTRB(Math.Max(0,(int)Math.Floor(left-mx)),Math.Max(0,(int)Math.Floor(top-my)),Math.Min(width,(int)Math.Ceiling(right+mx)),Math.Min(height,(int)Math.Ceiling(bottom+my)));
        }
        internal static bool Balanced(string tex){int depth=0;for(int i=0;i<tex.Length;i++){if(tex[i]=='\\'){i++;continue;}if(tex[i]=='{')depth++;if(tex[i]=='}'&&--depth<0)return false;}return depth==0;}

        internal bool TryRead(Bitmap source,PointF[] normalized,out string latex,out float confidence,Func<bool> canceled=null){
            latex="";confidence=0;if(canceled!=null&&canceled())throw new OperationCanceledException();
            Rectangle box=Bounds(normalized,source.Width,source.Height);if(box.IsEmpty)return false;
            lock(gate)using(var crop=source.Clone(box,PixelFormat.Format24bppRgb)){
                var plan=FormulaGeometry.Analyze(crop);
                // Separate only well-supported equation rows. Fraction bars,
                // roots and tall matrix fences remain within their expression.
                if(plan.Rows.Count>1&&plan.Rows.Count<=8){
                    var rows=new List<string>();double score=0;
                    foreach(Rectangle row in plan.Rows)using(var part=crop.Clone(row,PixelFormat.Format24bppRgb)){
                        string tex;float value;if(!Read(part,out tex,out value,canceled))return false;rows.Add(AlignedRow(tex));score+=value;
                    }
                    latex=@"\begin{aligned}"+String.Join(@" \\ ",rows)+@"\end{aligned}";confidence=(float)(score/rows.Count);return true;
                }
                return Read(crop,out latex,out confidence,canceled);
            }
        }
        bool Read(Bitmap crop,out string latex,out float confidence,Func<bool> canceled){
            if(canceled!=null&&canceled())throw new OperationCanceledException();byte[] pixels=TexoPixels.Prepare(crop);latex=worker.Recognize(pixels,384,384,out confidence,canceled);
            latex=Regex.Replace(latex,@"\\(begin|end)\s*\{\s*([^{}]+?)\s*\}",m=>"\\"+m.Groups[1].Value+"{"+m.Groups[2].Value.Replace(" ","")+"}");
            latex=Regex.Replace(latex,@"(\\(?:operatorname|mathrm)\s*\*?\s*)\{([A-Za-z ]+)\}",delegate(Match m){string value=m.Groups[2].Value.Replace(" ","");return Regex.IsMatch(value,@"^(?:sin|cos|tan|cot|sinh|cosh|tanh|log|ln|exp|min|max|lim|sup|inf|arg|det|rank|tr)$")?m.Groups[1].Value+"{"+value+"}":m.Value;});
            latex=Regex.Replace(latex,@"\{\s*([0-9][0-9 ]*)\s*\}",m=>"{"+m.Groups[1].Value.Replace(" ","")+"}");
            if(!Complete(latex)){latex="";confidence=0;return false;}return true;
        }
        internal static bool Complete(string tex){
            if(String.IsNullOrWhiteSpace(tex)||!Balanced(tex))return false;var stack=new Stack<string>();
            foreach(Match m in Regex.Matches(tex,@"\\(begin|end)\{([^{}]+)\}")){if(m.Groups[1].Value=="begin")stack.Push(m.Groups[2].Value);else if(stack.Count==0||stack.Pop()!=m.Groups[2].Value)return false;}return stack.Count==0;
        }
        internal static string AlignedRow(string tex){
            var begin=Regex.Match(tex,@"^\s*\\begin\{(array|aligned|gathered)\}\s*(?:\{[ lcr|]+\}\s*)?");
            if(begin.Success){string ending=@"\end{"+begin.Groups[1].Value+"}";if(tex.TrimEnd().EndsWith(ending,StringComparison.Ordinal)){
                string body=tex.TrimEnd().Substring(begin.Length,tex.TrimEnd().Length-begin.Length-ending.Length);var cells=new List<string>();int depth=0,start=0;bool multi=false;
                for(int i=0;i<body.Length;i++){if(body[i]=='\\'){if(i+1<body.Length&&body[i+1]=='\\'){multi=true;break;}i++;continue;}if(body[i]=='{')depth++;else if(body[i]=='}')depth--;else if(depth==0&&body[i]=='&'){cells.Add(body.Substring(start,i-start));start=i+1;}}
                if(!multi){cells.Add(body.Substring(start));if(cells.Count>=3&&OnlyRelation(cells[1]))return cells[0].Trim()+"&"+String.Join(" ",cells.GetRange(1,cells.Count-1)).Trim();if(cells.Count==2&&OnlyRelation(cells[0]))return "&"+cells[0].Trim()+cells[1].Trim();tex=String.Join(" ",cells).Trim();}
            }}
            int nesting=0;for(int i=0;i<tex.Length;i++){if(tex[i]=='{')nesting++;else if(tex[i]=='}')nesting--;else if(nesting==0&&(tex[i]=='='||tex[i]=='<'||tex[i]=='>'||tex[i]=='\\'&&Regex.IsMatch(tex.Substring(i),@"^\\(?:leq?|geq?|approx|neq?)\b")))return tex.Insert(i,"&");}
            return "&"+tex;
        }
        static bool OnlyRelation(string value){value=Regex.Replace(value,@"[{}\s]","");return Regex.IsMatch(value,@"^(?:=|<|>|\\(?:leq?|geq?|approx|neq?))$");}
        public void Dispose(){worker.Dispose();}
    }
}
