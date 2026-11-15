using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace MiniAI {
    // Texo's reference pipeline crops white margins, resizes the short edge,
    // thumbnails and centres the image on a black 384 x 384 canvas. Supplying
    // that canvas avoids the runtime's simplified raw-image resizer.
    internal static class TexoPixels {
        sealed class Image {internal byte[] Data;internal int Width,Height;}
        sealed class Taps {internal int Start;internal double[] Values;}
        static double Kernel(double x,bool linear){x=Math.Abs(x);if(linear)return x<1?1-x:0;if(x<1)return ((1.5*x-2.5)*x)*x+1;if(x<2)return ((-.5*x+2.5)*x-4)*x+2;return 0;}
        static Taps[] Table(int source,int target,bool linear){
            var result=new Taps[target];double scale=source/(double)target,filter=Math.Max(1,scale),support=(linear?1:2)*filter;
            for(int x=0;x<target;x++){double center=(x+.5)*scale;int start=Math.Max(0,(int)(center-support+.5)),end=Math.Min(source,(int)(center+support+.5));var values=new double[end-start];double sum=0;for(int i=0;i<values.Length;i++){values[i]=Kernel((i+start-center+.5)/filter,linear);sum+=values[i];}if(sum!=0)for(int i=0;i<values.Length;i++)values[i]/=sum;result[x]=new Taps{Start=start,Values=values};}return result;
        }
        static byte Clip(double value){return (byte)Math.Max(0,Math.Min(255,(int)Math.Floor(value+.5)));}
        static Image Resize(Image source,int width,int height,bool linear){
            var horizontal=Table(source.Width,width,linear);var vertical=Table(source.Height,height,linear);var temp=new byte[width*source.Height];var data=new byte[width*height];
            for(int y=0;y<source.Height;y++)for(int x=0;x<width;x++){var taps=horizontal[x];double value=0;for(int i=0;i<taps.Values.Length;i++)value+=source.Data[y*source.Width+taps.Start+i]*taps.Values[i];temp[y*width+x]=Clip(value);}
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){var taps=vertical[y];double value=0;for(int i=0;i<taps.Values.Length;i++)value+=temp[(taps.Start+i)*width+x]*taps.Values[i];data[y*width+x]=Clip(value);}
            return new Image{Data=data,Width=width,Height=height};
        }
        static Image Reduce(Image source,int fx,int fy){
            if(fx<=1&&fy<=1)return source;int w=(source.Width+fx-1)/fx,h=(source.Height+fy-1)/fy;var data=new byte[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){int endY=Math.Min(source.Height,(y+1)*fy),endX=Math.Min(source.Width,(x+1)*fx),n=(endY-y*fy)*(endX-x*fx),sum=0;for(int yy=y*fy;yy<endY;yy++)for(int xx=x*fx;xx<endX;xx++)sum+=source.Data[yy*source.Width+xx];data[y*w+x]=(byte)(((long)(sum+n/2)*((1<<24)/n))>>24);}
            return new Image{Data=data,Width=w,Height=h};
        }
        internal static byte[] Prepare(Bitmap bitmap){
            byte[] original=FormulaGeometry.Gray(bitmap,false);int min=255,max=0;long sum=0;foreach(byte v in original){min=Math.Min(min,v);max=Math.Max(max,v);sum+=v;}
            var canvas=new byte[384*384];if(min==max)return canvas;
            bool invert=(sum/(double)original.Length-min)/(max-min)<.5;if(invert){for(int i=0;i<original.Length;i++)original[i]=(byte)(255-original[i]);int a=min;min=255-max;max=255-a;}
            int left=bitmap.Width,right=0,top=bitmap.Height,bottom=0;
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if((original[y*bitmap.Width+x]-min)*255.0/(max-min)<200){left=Math.Min(left,x);right=Math.Max(right,x+1);top=Math.Min(top,y);bottom=Math.Max(bottom,y+1);}
            if(right<=left)return canvas;int width=right-left,height=bottom-top;var data=new byte[width*height];for(int y=0;y<height;y++)Array.Copy(original,(y+top)*bitmap.Width+left,data,y*width,width);
            var image=new Image{Data=data,Width=width,Height=height};double grow=384.0/Math.Min(width,height);int w=Math.Max(1,(int)(width*grow)),h=Math.Max(1,(int)(height*grow));
            // Bound intermediate storage for abnormally long, one-pixel crops.
            if((long)w*h<=8000000)image=Resize(image,w,h,true);
            double scale=Math.Min(1,384.0/Math.Max(image.Width,image.Height));w=Math.Max(1,(int)Math.Round(image.Width*scale));h=Math.Max(1,(int)Math.Round(image.Height*scale));
            int fx=Math.Max(1,(int)(image.Width/(double)w/2)),fy=Math.Max(1,(int)(image.Height/(double)h/2));image=Reduce(image,fx,fy);image=Resize(image,w,h,false);
            int dx=(384-w)/2,dy=(384-h)/2;for(int y=0;y<h;y++)Array.Copy(image.Data,y*w,canvas,(y+dy)*384+dx,w);return canvas;
        }
    }
}
