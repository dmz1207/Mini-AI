using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace MiniAI {
    // Layout decisions use distances in glyph heights, not publisher names or DPI.
    // Ink is retained: tiny components are excluded from row voting, never erased.
    internal static class FormulaGeometry {
        internal sealed class Plan {
            internal readonly List<Rectangle> Rows=new List<Rectangle>();
            internal Rectangle LeftFence,RightFence;
            internal float GlyphHeight;
        }
        sealed class Blob {internal Rectangle Box;internal int Area;}
        internal static byte[] Gray(Bitmap image,bool normalize=true) {
            using(var copy=new Bitmap(image.Width,image.Height,PixelFormat.Format24bppRgb)) {
                using(var g=Graphics.FromImage(copy)){g.Clear(Color.White);g.DrawImageUnscaled(image,0,0);}
                var data=copy.LockBits(new Rectangle(Point.Empty,copy.Size),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
                try {
                    int stride=Math.Abs(data.Stride);var bytes=new byte[stride*copy.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                    var gray=new byte[copy.Width*copy.Height];int min=255,max=0;long sum=0;
                    for(int y=0;y<copy.Height;y++)for(int x=0;x<copy.Width;x++) {
                        int p=(data.Stride<0?copy.Height-1-y:y)*stride+x*3;
                        byte value=(byte)((bytes[p+2]*30+bytes[p+1]*59+bytes[p]*11)/100);
                        gray[y*copy.Width+x]=value;min=Math.Min(min,value);max=Math.Max(max,value);sum+=value;
                    }
                    if(!normalize)return gray;
                    bool invert=max>min&&(sum/(double)gray.Length-min)/(max-min)<.5;
                    for(int i=0;i<gray.Length;i++){int value=max==min?255:(gray[i]-min)*255/(max-min);gray[i]=(byte)(invert?255-value:value);}
                    return gray;
                }finally{copy.UnlockBits(data);}
            }
        }
        internal static bool Label(Bitmap image,Rectangle row,float glyphHeight,out Rectangle body,out Rectangle label) {
            body=row;label=Rectangle.Empty;if(glyphHeight<5)return false;
            byte[] gray;using(var crop=image.Clone(row,PixelFormat.Format24bppRgb))gray=Gray(crop);var columns=new int[row.Width];
            for(int y=0;y<row.Height;y++)for(int x=0;x<row.Width;x++)if(gray[y*row.Width+x]<160)columns[x]++;
            int right=columns.Length-1;while(right>=0&&columns[right]==0)right--;if(right<0)return false;
            int at=right;while(at>=0&&columns[at]>0)at--;int labelLeft=at+1;
            // Join all glyphs of the short parenthesized number at the margin.
            while(at>=0){int end=at;while(at>=0&&columns[at]==0)at--;if(end-at>glyphHeight*.8)break;while(at>=0&&columns[at]>0)at--;labelLeft=at+1;}
            if(labelLeft<row.Width*.65||right-labelLeft>glyphHeight*4||at<0)return false;
            int gapRight=labelLeft-1;at=gapRight;while(at>=0&&columns[at]==0)at--;
            if(gapRight-at<glyphHeight*2.5||at<glyphHeight*2)return false;
            label=Rectangle.FromLTRB(row.Left+labelLeft,row.Top,row.Left+right+1,row.Bottom);
            body=Rectangle.FromLTRB(row.Left,row.Top,row.Left+at+2,row.Bottom);return true;
        }
        internal static Plan Analyze(Bitmap source) {
            double scale=Math.Min(1,Math.Sqrt(3000000.0/((long)source.Width*source.Height)));
            if(scale<1)using(var small=new Bitmap(source,Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale))))return AnalyzeCore(small,1/scale);
            return AnalyzeCore(source,1);
        }
        static Plan AnalyzeCore(Bitmap image,double scale) {
            int width=image.Width,height=image.Height;var gray=Gray(image);var seen=new bool[gray.Length];var queue=new List<int>();var blobs=new List<Blob>();
            for(int start=0;start<gray.Length;start++) {
                if(seen[start]||gray[start]>=185)continue;
                queue.Clear();queue.Add(start);seen[start]=true;int left=width,top=height,right=0,bottom=0;
                for(int n=0;n<queue.Count;n++) {
                    int at=queue[n],x=at%width,y=at/width;left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                    for(int yy=Math.Max(0,y-1);yy<=Math.Min(height-1,y+1);yy++)for(int xx=Math.Max(0,x-1);xx<=Math.Min(width-1,x+1);xx++) {
                        int next=yy*width+xx;if(!seen[next]&&gray[next]<185){seen[next]=true;queue.Add(next);}
                    }
                }
                blobs.Add(new Blob{Box=Rectangle.FromLTRB(left,top,right+1,bottom+1),Area=queue.Count});
            }
            var plan=new Plan();
            var sizes=blobs.Where(b=>b.Area>=5&&b.Box.Height>=5&&b.Box.Width<=b.Box.Height*8).Select(b=>b.Box.Height).OrderBy(x=>x).ToList();
            if(sizes.Count==0)return plan;
            float body=sizes[(int)((sizes.Count-1)*.60)];plan.GlyphHeight=(float)(body*scale);
            var anchors=blobs.Where(b=>b.Box.Height>=body*.62&&b.Box.Height<=body*1.65&&b.Box.Width<=body*8&&b.Area>=body*.8).ToList();
            if(anchors.Count<4)return plan;
            int inkLeft=blobs.Min(b=>b.Box.Left),inkRight=blobs.Max(b=>b.Box.Right),inkTop=blobs.Min(b=>b.Box.Top),inkBottom=blobs.Max(b=>b.Box.Bottom);
            var tall=blobs.Where(b=>b.Box.Height>body*1.8&&b.Box.Width<body*1.3).ToList();
            var leftFence=tall.Where(b=>b.Box.Left<=inkLeft+body*.7&&b.Box.Height>body*2.2).OrderByDescending(b=>b.Box.Height).FirstOrDefault();
            var rightFence=tall.Where(b=>b.Box.Right>=inkRight-body*.7&&b.Box.Height>body*2.2).OrderByDescending(b=>b.Box.Height).FirstOrDefault();
            int contentLeft=leftFence==null?inkLeft:leftFence.Box.Right+1,contentRight=rightFence==null?inkRight:rightFence.Box.Left-1;
            if(contentRight-contentLeft<body*2)return plan;
            var rows=new int[height];
            for(int y=inkTop;y<inkBottom;y++)for(int x=contentLeft;x<contentRight;x++)if(gray[y*width+x]<185)rows[y]++;
            // Protect numerator/bar/denominator groups and limits from row splitting.
            var protectedSpans=new List<Rectangle>();
            // PDF fonts sometimes build a matrix bracket from disconnected
            // vertical pieces. They must protect the enclosed rows as one unit.
            foreach(var stem in blobs.Where(b=>b.Box.Height>=body*1.3&&b.Box.Width<body*.4&&b.Area>b.Box.Width*b.Box.Height*.6)) {
                var chain=stem.Box;int pieces=1;
                foreach(var next in blobs.Where(b=>b!=stem&&b.Box.Top>=stem.Box.Bottom&&b.Box.Height>=body*1.3&&b.Box.Width<body*.4&&b.Area>b.Box.Width*b.Box.Height*.6).OrderBy(b=>b.Box.Top)) {
                    if(next.Box.Top-chain.Bottom>body*.65)break;
                    if(Math.Abs(next.Box.Left-stem.Box.Left)>Math.Max(2,body*.18))continue;
                    chain=Rectangle.Union(chain,next.Box);pieces++;
                }
                if(pieces>=2&&chain.Height>body*2.2)protectedSpans.Add(chain);
            }
            foreach(var bar in blobs.Where(b=>b.Box.Width>=body*.8&&b.Box.Height<=Math.Max(3,body*.22)&&b.Box.Width>b.Box.Height*4)) {
                var above=anchors.Where(b=>b.Box.Bottom<=bar.Box.Top+2&&bar.Box.Top-b.Box.Bottom<body*1.5&&b.Box.Left<bar.Box.Right&&b.Box.Right>bar.Box.Left).ToList();
                var below=anchors.Where(b=>b.Box.Top>=bar.Box.Bottom-2&&b.Box.Top-bar.Box.Bottom<body*1.5&&b.Box.Left<bar.Box.Right&&b.Box.Right>bar.Box.Left).ToList();
                if(above.Count>0&&below.Count>0)protectedSpans.Add(Rectangle.FromLTRB(bar.Box.Left,above.Min(b=>b.Box.Top),bar.Box.Right,below.Max(b=>b.Box.Bottom)));
            }
            var cuts=new List<int>();int run=-1;
            for(int y=inkTop;y<=inkBottom;y++) {
                bool empty=y<inkBottom&&rows[y]==0;
                if(empty&&run<0)run=y;
                if(empty||run<0)continue;
                int cut=(run+y)/2,gap=y-run;run=-1;
                if(gap<Math.Max(3,body*.32)||cut<=inkTop||cut>=inkBottom)continue;
                if(protectedSpans.Any(b=>b.Top<cut&&b.Bottom>cut))continue;
                if(blobs.Any(b=>b!=leftFence&&b!=rightFence&&b.Box.Top<cut&&b.Box.Bottom>cut))continue;
                cuts.Add(cut);
            }
            if(cuts.Count==0||cuts.Count>10)return plan;
            var boundaries=new List<int>{inkTop};boundaries.AddRange(cuts);boundaries.Add(inkBottom);
            for(int i=0;i+1<boundaries.Count;i++) {
                int top=boundaries[i],bottom=boundaries[i+1];
                var bodyInk=anchors.Where(b=>b.Box.Top>=top&&b.Box.Bottom<=bottom&&b.Box.Left>=contentLeft&&b.Box.Right<=contentRight).ToList();
                // A dot, accent, isolated script or fraction limit is not a line.
                if(bodyInk.Count<2) {plan.Rows.Clear();return plan;}
                int left=contentRight,right=contentLeft,first=bottom,last=top;
                for(int y=top;y<bottom;y++)for(int x=contentLeft;x<contentRight;x++)if(gray[y*width+x]<185){left=Math.Min(left,x);right=Math.Max(right,x+1);first=Math.Min(first,y);last=Math.Max(last,y+1);}
                if(right<=left){plan.Rows.Clear();return plan;}
                plan.Rows.Add(Scaled(Rectangle.FromLTRB(Math.Max(contentLeft,left-2),Math.Max(top,first-2),Math.Min(contentRight,right+2),Math.Min(bottom,last+2)),scale,sourceWidth: (int)Math.Round(width*scale),sourceHeight:(int)Math.Round(height*scale)));
            }
            if(leftFence!=null)plan.LeftFence=Scaled(leftFence.Box,scale,(int)Math.Round(width*scale),(int)Math.Round(height*scale));
            if(rightFence!=null)plan.RightFence=Scaled(rightFence.Box,scale,(int)Math.Round(width*scale),(int)Math.Round(height*scale));
            return plan;
        }
        static Rectangle Scaled(Rectangle box,double scale,int sourceWidth,int sourceHeight){return Rectangle.Intersect(new Rectangle(0,0,sourceWidth,sourceHeight),Rectangle.FromLTRB((int)Math.Floor(box.Left*scale),(int)Math.Floor(box.Top*scale),(int)Math.Ceiling(box.Right*scale),(int)Math.Ceiling(box.Bottom*scale)));}
    }
}
