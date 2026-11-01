using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MiniAI.Desktop {
    internal static class FormulaPreview {
        static FormulaPreview(){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args){if(new AssemblyName(args.Name).Name!="WpfMath")return null;using(var input=typeof(FormulaPreview).Assembly.GetManifestResourceStream("MiniAI.WpfMath")){if(input==null)return null;using(var memory=new MemoryStream()){input.CopyTo(memory);return Assembly.Load(memory.ToArray());}}};}
        internal static Bitmap Render(string tex){return RenderCore(FormulaMarkup.Clean(tex));}
        [MethodImpl(MethodImplOptions.NoInlining)]static Bitmap RenderCore(string tex){var formula=new WpfMath.TexFormulaParser().Parse(tex);var renderer=formula.GetRenderer(WpfMath.TexStyle.Display,30,"Cambria Math",System.Windows.Media.Brushes.White,System.Windows.Media.Brushes.Black);var size=renderer.RenderSize;if(size.Width>5000||size.Height>2000||size.Width*size.Height>4000000)throw new FormatException("Formula preview is too large.");double dpi=Math.Min(576,Math.Max(24,96*Math.Min(1800/(size.Width+20),800/(size.Height+20))));var bitmap=renderer.RenderToBitmap(10,10,dpi);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=new MemoryStream()){encoder.Save(stream);stream.Position=0;using(var image=Image.FromStream(stream))return new Bitmap(image);}}
    }
}
