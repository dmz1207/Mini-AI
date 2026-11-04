using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniAI {
    // RapidLaTeX OCR is kept separate from the normal text recognizer. The
    // detector supplies a region, then this encoder/decoder preserves the
    // two-dimensional layout as TeX (fractions, scripts, roots and matrices).
    internal sealed class FormulaOcr : IDisposable {
        // The pinned decoder has 512 learned positions, including the BOS token.
        const int Bos=1,Eos=2,MaxTokens=512,Vocabulary=8000;
        readonly OfflineRuntime runtime;
        readonly OfflineRuntime.Session resizer,encoder,decoder;
        readonly string[] vocabulary;
        readonly string modelRoot;
        OfflineOcr labelOcr;
        readonly object gate=new object();
        internal FormulaOcr(string root) {
            modelRoot=root;
            string folder=Path.Combine(root,"models","formula");
            Guard.Check(File.Exists(Path.Combine(folder,"encoder.onnx"))&&File.Exists(Path.Combine(folder,"decoder.onnx"))&&File.Exists(Path.Combine(folder,"tokenizer.json")),"The formula OCR model is missing.");
            Guard.Check(File.Exists(Path.Combine(folder,"image_resizer.onnx")),"The formula OCR resizer model is missing.");runtime=new OfflineRuntime(Path.Combine(root,"runtime","onnxruntime.dll"));
            try {resizer=runtime.Open(Path.Combine(folder,"image_resizer.onnx"),1,1);encoder=runtime.Open(Path.Combine(folder,"encoder.onnx"),1,1);decoder=runtime.Open(Path.Combine(folder,"decoder.onnx"),3,1);vocabulary=LoadVocabulary(Path.Combine(folder,"tokenizer.json"));}
            catch {Dispose();throw;}
        }
        internal static bool Available(string root){string folder=Path.Combine(root,"models","formula");return File.Exists(Path.Combine(folder,"image_resizer.onnx"))&&File.Exists(Path.Combine(folder,"encoder.onnx"))&&File.Exists(Path.Combine(folder,"decoder.onnx"))&&File.Exists(Path.Combine(folder,"tokenizer.json"));}
        static string[] LoadVocabulary(string path) {
            var root=Json.Read(File.ReadAllText(path,Encoding.UTF8));var model=(Dictionary<string,object>)root["model"];var map=(Dictionary<string,object>)model["vocab"];var result=new string[Vocabulary];
            foreach(var item in map){int id=Convert.ToInt32(item.Value);if(id>=0&&id<result.Length)result[id]=item.Key;}
            return result;
        }
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
        // Match RapidLaTeXOCR's contrast/inversion, tight ink crop and 32-pixel
        // padding. The resizer predicts a target width, not a maximum width:
        // retaining that distinction also permits enlargement of small glyphs.
        static Bitmap Pad(Bitmap source) {
            var gray=FormulaGeometry.Gray(source);int left=source.Width,top=source.Height,right=-1,bottom=-1;
            for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)if(gray[y*source.Width+x]<128){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            if(right<left)throw new IOException("No formula ink found in the selected region.");int w=right-left+1,h=bottom-top+1;var result=new Bitmap((w+31)/32*32,(h+31)/32*32,PixelFormat.Format24bppRgb);
            var bits=result.LockBits(new Rectangle(Point.Empty,result.Size),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
            try{var bytes=new byte[Math.Abs(bits.Stride)*result.Height];for(int i=0;i<bytes.Length;i++)bytes[i]=255;for(int y=0;y<h;y++)for(int x=0;x<w;x++){byte g=gray[(y+top)*source.Width+x+left];int at=y*bits.Stride+x*3;bytes[at]=bytes[at+1]=bytes[at+2]=g;}Marshal.Copy(bytes,0,bits.Scan0,bytes.Length);}finally{result.UnlockBits(bits);}return result;
        }
        static Bitmap Bounded(Bitmap source,int width,int height) {
            double ratio=Math.Min(1,Math.Min(672.0/width,192.0/height));width=Math.Max(1,(int)(width*ratio));height=Math.Max(1,(int)(height*ratio));var result=new Bitmap(Math.Max(32,width),Math.Max(32,height),PixelFormat.Format24bppRgb);using(var graphics=Graphics.FromImage(result)){graphics.Clear(Color.White);graphics.InterpolationMode=width>source.Width?InterpolationMode.HighQualityBilinear:InterpolationMode.HighQualityBicubic;graphics.DrawImage(source,new Rectangle(0,0,width,height),new Rectangle(Point.Empty,source.Size),GraphicsUnit.Pixel);}return result;
        }
        static float[] Pixels(Bitmap source,out long[] shape) {
            byte[] gray=FormulaGeometry.Gray(source,false);var result=new float[gray.Length];for(int i=0;i<gray.Length;i++)result[i]=(gray[i]/255f-0.7931f)/0.1738f;shape=new long[]{1,1,source.Height,source.Width};return result;
        }
        float[] Resize(Bitmap source,out long[] shape,Func<bool> canceled) {
            using(var padded=Pad(source))using(var initial=Bounded(padded,padded.Width,padded.Height)){int width=initial.Width,height=initial.Height;double ratio=1;float[] pixels=null;shape=null;for(int n=0;n<10;n++){if(canceled!=null&&canceled())throw new OperationCanceledException();height=Math.Max(1,(int)(height*ratio));using(var resized=Bounded(initial,width,height))using(var final=Pad(resized)){pixels=Pixels(final,out shape);var prediction=resizer.Run(pixels,shape);int best=0;for(int i=1;i<prediction.Data.Length;i++)if(prediction.Data[i]>prediction.Data[best])best=i;width=Math.Max(32,Math.Min(672,(best+1)*32));if(width==final.Width)break;ratio=width/(double)final.Width;}}return pixels;}
        }
        static string Decode(string[] vocab,List<long> ids) {
            var bytes=new List<byte>();foreach(long value in ids){if(value<0||value>=vocab.Length||value==Bos||value==Eos||value==0)continue;string token=vocab[(int)value];if(String.IsNullOrEmpty(token))continue;foreach(char c in token){if(c>=33&&c<=126||c>=161&&c<=172||c>=174&&c<=255)bytes.Add((byte)c);else {int decoded=(int)c-256;bytes.Add((byte)(decoded>=0&&decoded<=255?decoded:c=='Ġ'?32:c=='Ċ'?10:63));}}}
            string text=Encoding.UTF8.GetString(bytes.ToArray()).Trim();text=Regex.Replace(text,@"(?i)(\\(?:operatorname|mathrm|text|mathbf)\s?\*?\s*)\{(.*?)\}",delegate(Match m){return m.Groups[1].Value+"{"+m.Groups[2].Value.Replace(" ","")+"}";});
            string before;do{before=text;text=Regex.Replace(text,@"(?<=[^a-zA-Z\\\s])\s+(?=[^a-zA-Z])","");text=Regex.Replace(text,@"(?<!\\)\s+(?=[{}_^])","");}while(before!=text);return text;
        }
        internal bool TryRead(Bitmap source,PointF[] normalized,out string latex,out float confidence,Func<bool> canceled=null) {
            latex="";confidence=0;if(canceled!=null&&canceled())throw new OperationCanceledException();Rectangle box=Bounds(normalized,source.Width,source.Height);if(box.IsEmpty)return false;
            lock(gate)using(var crop=new Bitmap(box.Width,box.Height,PixelFormat.Format24bppRgb)) {
                using(var graphics=Graphics.FromImage(crop)){graphics.Clear(Color.White);graphics.DrawImage(source,new Rectangle(0,0,box.Width,box.Height),box,GraphicsUnit.Pixel);}
                var plan=FormulaGeometry.Analyze(crop);
                if(plan.Rows.Count>1&&plan.Rows.Count<=8) {
                    // Compact two/three-line expressions often work best as a
                    // whole. A bounded first attempt preserves that capability.
                    if(plan.Rows.Count<=3&&ReadSingle(crop,out latex,out confidence,canceled,256)&&Balanced(latex))return true;
                    if(ReadRows(crop,plan,out latex,out confidence,canceled))return true;
                }
                return ReadPart(crop,new Rectangle(Point.Empty,crop.Size),plan.GlyphHeight,out latex,out confidence,canceled);
            }
        }
        static Bitmap Part(Bitmap source,Rectangle box) {box=Rectangle.Intersect(new Rectangle(Point.Empty,source.Size),box);return source.Clone(box,PixelFormat.Format24bppRgb);}
        Rectangle Body(Bitmap source,Rectangle box,float glyphHeight,Func<bool> canceled) {
            Rectangle body,label;
            if(FormulaGeometry.Label(source,box,glyphHeight,out body,out label)) {
                string number="";float score=0;
                using(var numberCrop=Part(source,label)) {
                    bool read=false;
                    if(File.Exists(Path.Combine(modelRoot,"models/ocr-fast/rec.onnx"))&&File.Exists(Path.Combine(modelRoot,"models/ocr-fast/det.onnx"))&&File.Exists(Path.Combine(modelRoot,"models/ocr-fast/keys.txt"))) {
                        if(labelOcr==null)labelOcr=new OfflineOcr(modelRoot,"fast");
                        if(canceled!=null&&canceled())throw new OperationCanceledException();
                        var value=labelOcr.ReadLine(numberCrop);
                        if(value!=null&&value.Confidence>=.85f){number=value.Text;read=true;}
                    }else read=ReadSingle(numberCrop,out number,out score,canceled,20)&&score>=.85f;
                    if(read) {
                    number=Regex.Replace(number,@"\\(?:left|right)|\s|[{}]","");
                    if(Regex.IsMatch(number,@"^\([0-9]{1,4}(?:\.[0-9]{1,3})?[a-z]?\)$"))box=body;
                    }
                }
            }
            return box;
        }
        bool ReadPart(Bitmap source,Rectangle box,float glyphHeight,out string latex,out float confidence,Func<bool> canceled,int limit=MaxTokens) {
            box=Body(source,box,glyphHeight,canceled);
            using(var crop=Part(source,box))return ReadSingle(crop,out latex,out confidence,canceled,limit);
        }
        bool Fence(Bitmap source,Rectangle box,bool left,out string symbol,Func<bool> canceled) {
            symbol="";if(box.IsEmpty)return true;string tex;float score;
            box.Inflate(3,3);using(var crop=Part(source,box))if(!ReadSingle(crop,out tex,out score,canceled,24))return false;
            tex=Regex.Replace(tex,@"\s|\\(?:left|right|big|Big|bigg|Bigg)","");
            if(tex=="{"||tex==@"\{")symbol=@"\{";else if(tex=="}"||tex==@"\}")symbol=@"\}";else if(tex=="("||tex==")"||tex=="["||tex=="]")symbol=tex;else return false;
            return left?(symbol==@"\{"||symbol=="("||symbol=="["):(symbol==@"\}"||symbol==")"||symbol=="]");
        }
        bool ReadRows(Bitmap source,FormulaGeometry.Plan plan,out string latex,out float confidence,Func<bool> canceled) {
            latex="";confidence=0;string left,right;if(!Fence(source,plan.LeftFence,true,out left,canceled)||!Fence(source,plan.RightFence,false,out right,canceled))return false;
            var rows=new List<string>();double score=0;
            foreach(Rectangle box in plan.Rows) {
                string tex;float value;if(!ReadPart(source,box,plan.GlyphHeight,out tex,out value,canceled)||!Balanced(tex))return false;
                int relation=Relation(tex);rows.Add(relation<0?"&"+tex:tex.Insert(relation,"&"));score+=value;
            }
            latex=@"\begin{aligned}"+String.Join(@" \\ ",rows)+@"\end{aligned}";
            if(left.Length>0||right.Length>0)latex=@"\left"+(left.Length==0?".":left)+latex+@"\right"+(right.Length==0?".":right);
            confidence=(float)(score/rows.Count);return true;
        }
        static int Relation(string tex){int depth=0;for(int i=0;i<tex.Length;i++){if(tex[i]=='{'&&(i==0||tex[i-1]!='\\'))depth++;else if(tex[i]=='}'&&(i==0||tex[i-1]!='\\'))depth--;else if(depth==0&&"=<>".IndexOf(tex[i])>=0)return i;}return -1;}
        internal static bool Balanced(string tex){int depth=0;for(int i=0;i<tex.Length;i++){if(tex[i]=='\\'){i++;continue;}if(tex[i]=='{')depth++;if(tex[i]=='}'&&--depth<0)return false;}return depth==0;}
        bool ReadSingle(Bitmap crop,out string latex,out float confidence,Func<bool> canceled,int limit=MaxTokens) {
            long[] shape;float[] pixels=Resize(crop,out shape,canceled);return DecodeImage(pixels,shape,out latex,out confidence,canceled,limit);
        }
        bool DecodeImage(float[] pixels,long[] imageShape,out string latex,out float confidence,Func<bool> canceled,int limit) {
            latex="";confidence=0;OfflineRuntime.Tensor context=encoder.Run(pixels,imageShape);var ids=new List<long>{Bos};var mask=new byte[1];double score=0;int count=0;
                bool ended=false;for(int step=0;step<limit;step++){if(canceled!=null&&canceled())throw new OperationCanceledException();int length=ids.Count;var inputIds=ids.ToArray();mask=new byte[length];for(int i=0;i<mask.Length;i++)mask[i]=1;var output=decoder.Run(new[]{OfflineRuntime.InputData.Int64(inputIds,new long[]{1,length}),OfflineRuntime.InputData.Bool(mask,new long[]{1,length}),OfflineRuntime.InputData.Float(context.Data,context.Shape)});int offset=(length-1)*Vocabulary,best=0;float bestValue=Single.NegativeInfinity,second=Single.NegativeInfinity;for(int i=0;i<Vocabulary&&offset+i<output.Data.Length;i++){float value=output.Data[offset+i];if(value>bestValue){second=bestValue;bestValue=value;best=i;}else if(value>second)second=value;}if(best==Eos||best==0){ended=true;break;}ids.Add(best);score+=1.0/(1.0+Math.Exp(-(bestValue-second)));count++;}
                if(!ended)return false;latex=Decode(vocabulary,ids);confidence=count==0?0:(float)(score/count);return latex.Length>0&&count>0;
        }
        public void Dispose(){if(labelOcr!=null)labelOcr.Dispose();if(decoder!=null)decoder.Dispose();if(encoder!=null)encoder.Dispose();if(resizer!=null)resizer.Dispose();if(runtime!=null)runtime.Dispose();}
    }
}
