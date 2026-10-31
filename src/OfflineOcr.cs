using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniAI {
    internal sealed class OcrLine {
        internal string Text;
        internal float Confidence;
        internal PointF[] Corners;
    }
    internal sealed class OfflineOcr : IDisposable {
        readonly OfflineRuntime runtime;
        readonly OfflineRuntime.Session detector,recognizer;
        readonly string[] keys;
        internal OfflineOcr(string root) {
            try{runtime=new OfflineRuntime(Path.Combine(root,"runtime","onnxruntime.dll"));detector=runtime.Open(Path.Combine(root,"models","ocr","det.onnx"));recognizer=runtime.Open(Path.Combine(root,"models","ocr","rec.onnx"));var chars=new List<string>{""};chars.AddRange(File.ReadAllLines(Path.Combine(root,"models","ocr","keys.txt"),Encoding.UTF8));chars.Add(" ");keys=chars.ToArray();}catch{Dispose();throw;}
        }
        static float[] Pixels(Bitmap source,int width,int height,bool detection) {
            using(var image=new Bitmap(width,height,PixelFormat.Format24bppRgb)) {
                using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.White);graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;graphics.DrawImage(source,new Rectangle(0,0,width,height));}
                var bits=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
                try {
                    var data=new byte[Math.Abs(bits.Stride)*height];Marshal.Copy(bits.Scan0,data,0,data.Length);var result=new float[3*width*height];float[] mean={0.485f,0.456f,0.406f},std={0.229f,0.224f,0.225f};
                    for(int y=0;y<height;y++)for(int x=0;x<width;x++)for(int c=0;c<3;c++){float value=data[y*bits.Stride+x*3+c]/255f;result[c*width*height+y*width+x]=detection?(value-mean[c])/std[c]:(value-0.5f)*2;}
                    return result;
                }finally{image.UnlockBits(bits);}
            }
        }
        sealed class Region {
            internal float X,Y,Width,Height,Angle;
            internal PointF[] Points() {
                float a=(float)Math.Cos(Angle),b=(float)Math.Sin(Angle),w=Width/2,h=Height/2;
                return new[]{new PointF(X-a*w+b*h,Y-b*w-a*h),new PointF(X+a*w+b*h,Y+b*w-a*h),new PointF(X+a*w-b*h,Y+b*w+a*h),new PointF(X-a*w-b*h,Y-b*w+a*h)};
            }
        }
        static List<Region> Regions(OfflineRuntime.Tensor tensor) {
            Guard.Check(tensor.Shape.Length==4&&tensor.Shape[0]==1&&tensor.Shape[1]==1,"Unexpected OCR detection model output.");
            int width=(int)tensor.Shape[3],height=(int)tensor.Shape[2];var values=tensor.Data;var seen=new bool[values.Length];var queue=new List<int>();var result=new List<Region>();
            for(int pixel=0;pixel<values.Length;pixel++) {
                if(seen[pixel]||values[pixel]<0.3f)continue;queue.Clear();queue.Add(pixel);seen[pixel]=true;double sx=0,sy=0,sxx=0,syy=0,sxy=0,score=0;
                for(int j=0;j<queue.Count;j++) {
                    int at=queue[j],x=at%width,y=at/width;sx+=x;sy+=y;sxx+=(double)x*x;syy+=(double)y*y;sxy+=(double)x*y;score+=values[at];
                    for(int yy=Math.Max(0,y-1);yy<=Math.Min(height-1,y+1);yy++)for(int xx=Math.Max(0,x-1);xx<=Math.Min(width-1,x+1);xx++){int n=yy*width+xx;if(!seen[n]&&values[n]>=0.3f){seen[n]=true;queue.Add(n);}}
                }
                int count=queue.Count;if(count<6||score/count<0.5)continue;double cx=sx/count,cy=sy/count,angle=0.5*Math.Atan2(2*(sxy/count-cx*cy),sxx/count-cx*cx-(syy/count-cy*cy));
                if(Math.Abs(angle)>Math.PI/6&&Math.Abs(angle)<Math.PI/3)angle=0;
                double ca=Math.Cos(angle),sa=Math.Sin(angle),loU=Double.MaxValue,hiU=Double.MinValue,loV=Double.MaxValue,hiV=Double.MinValue;
                foreach(int at in queue){double x=at%width-cx,y=at/width-cy,u=x*ca+y*sa,v=-x*sa+y*ca;loU=Math.Min(loU,u);hiU=Math.Max(hiU,u);loV=Math.Min(loV,v);hiV=Math.Max(hiV,v);}
                double w=hiU-loU+1,h=hiV-loV+1;if(h<2||w<3)continue;double margin=Math.Max(1,w*h*1.5/(2*(w+h))),u0=(loU+hiU)/2,v0=(loV+hiV)/2;
                result.Add(new Region{X=(float)(cx+ca*u0-sa*v0),Y=(float)(cy+sa*u0+ca*v0),Width=(float)(w+2*margin),Height=(float)(h+2*margin),Angle=(float)angle});
                if(result.Count>=5000)throw new IOException("OCR found too many text regions on this page.");
            }
            return ReadingOrder(result);
        }
        static List<Region> Rows(List<Region> items) {
            items.Sort(delegate(Region a,Region b){return a.Y.CompareTo(b.Y);});
            for(int i=0;i<items.Count;){int end=i+1;float row=items[i].Y,tolerance=Math.Max(3,items[i].Height*0.4f);while(end<items.Count&&Math.Abs(items[end].Y-row)<=tolerance)end++;items.Sort(i,end-i,Comparer<Region>.Create(delegate(Region a,Region b){return a.X.CompareTo(b.X);}));i=end;}
            return items;
        }
        // Infer a persistent central gutter; full-width headings split the column bands.
        static List<Region> ReadingOrder(List<Region> items) {
            if(items.Count<8)return Rows(items);float left=Single.MaxValue,right=Single.MinValue;
            foreach(var item in items){left=Math.Min(left,item.X-item.Width/2);right=Math.Max(right,item.X+item.Width/2);}
            float split=0;int best=0;
            for(int n=0;n<=64;n++) {
                float x=left+(right-left)*(0.35f+0.3f*n/64),margin=(right-left)*0.012f;int a=0,b=0,cross=0;
                foreach(var item in items){if(item.X+item.Width/2<x-margin)a++;else if(item.X-item.Width/2>x+margin)b++;else cross++;}
                int score=Math.Min(a,b)-3*cross;if(a>=3&&b>=3&&cross<=Math.Max(2,items.Count/5)&&score>best){best=score;split=x;}
            }
            if(best==0)return Rows(items);
            var spans=new List<Region>();var columns=new List<Region>();
            foreach(var item in items)if(item.X-item.Width/2<split&&item.X+item.Width/2>split)spans.Add(item);else columns.Add(item);
            Rows(spans);var ordered=new List<Region>();var band=new List<Region>();
            foreach(var span in spans){for(int i=columns.Count-1;i>=0;i--)if(columns[i].Y<span.Y-span.Height*0.4f){band.Add(columns[i]);columns.RemoveAt(i);}AppendColumns(ordered,band,split);band.Clear();ordered.Add(span);}
            AppendColumns(ordered,columns,split);return ordered;
        }
        static void AppendColumns(List<Region> output,List<Region> band,float split){var a=new List<Region>();var b=new List<Region>();foreach(var item in band)(item.X<split?a:b).Add(item);output.AddRange(Rows(a));output.AddRange(Rows(b));}
        sealed class Candidate {internal int Index,Width,Height,RecWidth;internal Region Region;internal PointF[] Corners;}
        internal List<OcrLine> Read(Bitmap image,Action<int,int> progress) {
            double scale=Math.Min(1,1536.0/Math.Max(image.Width,image.Height));int width=Math.Max(32,(int)Math.Round(image.Width*scale/32)*32),height=Math.Max(32,(int)Math.Round(image.Height*scale/32)*32);
            var tensor=detector.Run(Pixels(image,width,height,true),new long[]{1,3,height,width});var regions=Regions(tensor);var candidates=new List<Candidate>();float sx=image.Width/(float)width,sy=image.Height/(float)height;
            for(int i=0;i<regions.Count;i++) {
                var region=regions[i];var corners=region.Points();for(int c=0;c<4;c++)corners[c]=new PointF(corners[c].X*sx,corners[c].Y*sy);
                float rw=(float)Math.Sqrt(Math.Pow(corners[1].X-corners[0].X,2)+Math.Pow(corners[1].Y-corners[0].Y,2)),rh=(float)Math.Sqrt(Math.Pow(corners[3].X-corners[0].X,2)+Math.Pow(corners[3].Y-corners[0].Y,2));
                if(rw<3||rh<3||rw>image.Width*2||rh>image.Height*2)continue;
                candidates.Add(new Candidate{Index=i,Width=(int)Math.Ceiling(rw),Height=(int)Math.Ceiling(rh),RecWidth=Math.Max(32,Math.Min(2048,(int)Math.Ceiling(rw*48/rh))),Region=region,Corners=corners});
            }
            // Similar-width batches reuse model weights while keeping padding and memory bounded.
            candidates.Sort(delegate(Candidate a,Candidate b){return a.RecWidth.CompareTo(b.RecWidth);});var recognized=new OcrLine[regions.Count];
            int batchSize=Environment.GetEnvironmentVariable("MINIAI_OCR_BATCH")=="1"?1:4;
            for(int first=0;first<candidates.Count;first+=batchSize) {
                if(progress!=null)progress(first,candidates.Count);int count=Math.Min(batchSize,candidates.Count-first),recWidth=candidates[first+count-1].RecWidth;var data=new float[count*3*48*recWidth];
                for(int n=0;n<count;n++) {
                    var item=candidates[first+n];using(var crop=new Bitmap(item.Width,item.Height,PixelFormat.Format24bppRgb)) {
                        using(var graphics=Graphics.FromImage(crop)){graphics.Clear(Color.White);graphics.TranslateTransform(item.Width/2f,item.Height/2f);graphics.RotateTransform(-item.Region.Angle*180/(float)Math.PI);graphics.TranslateTransform(-item.Region.X*sx,-item.Region.Y*sy);graphics.DrawImageUnscaled(image,0,0);}
                        var pixels=Pixels(crop,item.RecWidth,48,false);for(int channel=0;channel<3;channel++)for(int y=0;y<48;y++)Array.Copy(pixels,(channel*48+y)*item.RecWidth,data,(n*3*48+channel*48+y)*recWidth,item.RecWidth);
                    }
                }
                var scores=recognizer.Run(data,new long[]{count,3,48,recWidth});Guard.Check(scores.Shape.Length==3&&scores.Shape[0]==count,"Unexpected OCR recognition model output.");int steps=(int)scores.Shape[1],vocabulary=(int)scores.Shape[2];Guard.Check(vocabulary==keys.Length,"The OCR dictionary does not match the recognition model.");
                for(int n=0;n<count;n++) {
                    var text=new StringBuilder();int previous=0,letters=0;double confidence=0;
                    for(int t=0;t<steps;t++){int offset=(n*steps+t)*vocabulary,best=0;float score=scores.Data[offset];for(int c=1;c<vocabulary;c++)if(scores.Data[offset+c]>score){best=c;score=scores.Data[offset+c];}if(best!=0&&best!=previous){text.Append(keys[best]);confidence+=score;letters++;}previous=best;}
                    string value=text.ToString().Trim();if(value.Length>0&&letters>0&&confidence/letters>=0.45){var item=candidates[first+n];for(int c=0;c<4;c++)item.Corners[c]=new PointF(Math.Max(0,Math.Min(1,item.Corners[c].X/image.Width)),Math.Max(0,Math.Min(1,item.Corners[c].Y/image.Height)));recognized[item.Index]=new OcrLine{Text=value,Confidence=(float)(confidence/letters),Corners=item.Corners};}
                }
            }
            if(progress!=null)progress(candidates.Count,candidates.Count);var output=new List<OcrLine>();foreach(var line in recognized)if(line!=null)output.Add(line);return output;
        }
        public void Dispose(){if(recognizer!=null)recognizer.Dispose();if(detector!=null)detector.Dispose();if(runtime!=null)runtime.Dispose();}
    }
}
