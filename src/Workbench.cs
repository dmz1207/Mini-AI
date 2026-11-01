using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using MiniAI.Desktop;

namespace MiniAI {
    internal sealed class Workbench : Form {
        readonly Icon applicationIcon;
        readonly string root;
        readonly TextBox input,output;
        readonly ComboBox language;
        readonly Label status;
        readonly Button translate,cancel;
        readonly CheckBox autoTranslate;
        readonly BubbleBackend backend;
        AiHotkey hotkey;
        TranslationBubble bubble;
        int generation;
        internal Workbench(string directory,string address) {
            applicationIcon=Icon.ExtractAssociatedIcon(typeof(Workbench).Assembly.Location);Icon=applicationIcon;
            root=directory;Text="MiniAI — Offline OCR & Translation";Font=new Font("Segoe UI",10);ClientSize=new Size(780,590);MinimumSize=new Size(630,460);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.White;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            backend=new BubbleBackend{Text=English,Translate=TranslateText,Recognize=Recognize};
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=6};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,50));layout.RowStyles.Add(new RowStyle(SizeType.Percent,50));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
            var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true};language=new ComboBox{Width=160,DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat};language.Items.Add("Auto Chinese / English");language.Items.AddRange(DesktopLanguages.Labels);language.SelectedIndex=0;bar.Controls.Add(language);
            translate=Button("Translate / 翻译",StartTranslation);bar.Controls.Add(translate);bar.Controls.Add(Button("Paste / 粘贴",PasteInput));layout.Controls.Add(bar,0,0);
            var images=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true};images.Controls.Add(Button("Open photo / 打开照片…",OpenImage));images.Controls.Add(Button("Screenshot OCR / 截图识别",CaptureOcr));images.Controls.Add(Button("Screen translate / 截图翻译",CaptureScreen));autoTranslate=new CheckBox{Text="Translate after OCR / 识别后翻译",AutoSize=true,Margin=new Padding(8,7,0,0)};images.Controls.Add(autoTranslate);layout.Controls.Add(images,0,1);
            input=Area(false);input.Name="TranslationSource";input.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.Enter){StartTranslation();e.SuppressKeyPress=true;}else if(e.Control&&e.KeyCode==Keys.V&&Clipboard.ContainsImage()){PasteInput();e.SuppressKeyPress=true;}};layout.Controls.Add(input,0,2);
            input.MouseUp+=delegate{if(input.SelectionLength<2)return;if(bubble!=null&&!bubble.IsDisposed)bubble.Close();bubble=new TranslationBubble(backend,new Rectangle(Cursor.Position,new Size(1,1)),false);bubble.Show(this);bubble.Translate(input.SelectedText);};
            output=Area(true);output.Name="TranslationResult";layout.Controls.Add(output,0,3);
            status=new Label{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,8,0,8),Text="PP-OCRv6 medium · Hy-MT2 1.25-bit · "+address+"\nDrop a photo, paste an image, or capture a region / 拖入照片、粘贴图片或截图；Alt+S 截图翻译"};layout.Controls.Add(status,0,4);
            var footer=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};footer.Controls.Add(Button("Copy OCR / 复制原文",delegate{if(input.TextLength>0)Clipboard.SetText(input.Text);}));footer.Controls.Add(Button("Copy result / 复制译文",delegate{if(output.TextLength>0)Clipboard.SetText(output.Text);}));footer.Controls.Add(Button("Save text / 保存文字",SaveText));cancel=Button("Cancel / 取消",Cancel);cancel.Enabled=false;footer.Controls.Add(cancel);footer.Controls.Add(Button("Copy API connection",delegate{Clipboard.SetText(File.ReadAllText(Path.Combine(root,"state","connection.json")));}));footer.Controls.Add(Button("Stop service / 停止服务",delegate{try{Request("/v1/shutdown",new{stop=true},null);Close();}catch(Exception error){status.Text=error.Message;}}));layout.Controls.Add(footer,0,5);
            EnableDrop(this);
            Shown+=delegate{hotkey=new AiHotkey(CaptureScreen);if(!hotkey.Registered)status.Text+="\nAlt+S is owned by another app; the Screen button still works.";};
        }
        TextBox Area(bool readOnly){return new TextBox{Multiline=true,ReadOnly=readOnly,Dock=DockStyle.Fill,Font=new Font("Segoe UI",11),ScrollBars=ScrollBars.Vertical,AcceptsReturn=true,WordWrap=true,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,8,0,0),MaxLength=100000};}
        Button Button(string title,Action action){var button=new Button{Text=title,AutoSize=true,MinimumSize=new Size(64,30),FlatStyle=FlatStyle.Flat};button.FlatAppearance.BorderColor=Color.FromArgb(215,222,230);button.Click+=delegate{action();};return button;}
        static string English(string text){var values=new Dictionary<string,string>{{"复制","Copy"},{"中英自动互译","Auto ZH/EN"},{"正在翻译…","Translating…"},{"正在识别选区…","Recognizing selection…"},{"选区内未识别出文字，请重新框选。","No text found. Select another region."},{"本机翻译 · 移开鼠标收起 · 按住 Ctrl 固定","Offline · Move away to dismiss · Hold Ctrl to keep"},{"可选中识别文字再译 · Ctrl+Enter 重译 · Ctrl 固定","Select OCR text to translate · Ctrl+Enter retry · Ctrl to keep"}};string result;return values.TryGetValue(text,out result)?result:text;}
        Dictionary<string,object> Request(string path,object data,Func<bool> canceled) {
            var state=Json.Read(File.ReadAllText(Path.Combine(root,"state","connection.json")));string address=Json.Get(state,"url");Uri uri;Guard.Check(Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Host=="127.0.0.1"&&uri.Scheme=="http"&&uri.AbsolutePath=="/"&&uri.UserInfo.Length==0,"Invalid MiniAI local address.");return Json.Request(address+path,data,Json.Get(state,"token"),600,canceled);
        }
        string TranslateText(string text,string target,Func<bool> canceled) {
            Guard.Check(text.Length<=100000,"Select at most 100000 characters.");var parts=new List<string>();
            foreach(string paragraph in Regex.Split(Regex.Replace(text,@"(?<=\p{L})-\r?\n(?=\p{L})",""),@"\r?\n\s*\r?\n")){string value=Regex.Replace(paragraph,@"\r?\n"," ").Trim();while(value.Length>900){int cut=900;for(int i=900;i>=450;i--)if(Char.IsWhiteSpace(value[i])||"。！？.!?;；".IndexOf(value[i])>=0){cut=i+1;break;}if(Char.IsHighSurrogate(value[cut-1]))cut--;parts.Add(value.Substring(0,cut).Trim());value=value.Substring(cut).Trim();}if(value.Length>0)parts.Add(value);}
            var translated=new List<string>();foreach(string part in parts){if(canceled())throw new OperationCanceledException();var reply=Request("/v1/translate",new{text=part,target=target},canceled);Guard.Check(!Convert.ToBoolean(reply["truncated"]),"Select a shorter passage; the output limit was reached.");translated.Add(Json.Get(reply,"text"));}return String.Join(Environment.NewLine+Environment.NewLine,translated);
        }
        string Recognize(byte[] image,Func<bool> canceled){var reply=Request("/v1/ocr",new{image_base64=Convert.ToBase64String(image)},canceled);return Json.Get(reply,"text");}
        void Cancel(){Interlocked.Increment(ref generation);translate.Enabled=true;cancel.Enabled=false;}
        void StartTranslation() {
            if(String.IsNullOrWhiteSpace(input.Text))return;Cancel();int serial=Volatile.Read(ref generation);string text=input.SelectedText.Length>0?input.SelectedText:input.Text,target=language.SelectedIndex==0?DesktopLanguages.AutoTarget(text):DesktopLanguages.Codes[language.SelectedIndex-1];translate.Enabled=false;cancel.Enabled=true;output.Text="Translating / 正在翻译…";
            new Thread(delegate(){try{string result=TranslateText(text,target,delegate{return serial!=Volatile.Read(ref generation);});Post(serial,delegate{output.Text=result;translate.Enabled=true;cancel.Enabled=false;status.Text="Done / 完成 · Offline on this computer / 在本机完成";});}catch(Exception error){Post(serial,delegate{output.Text=error.Message;translate.Enabled=true;cancel.Enabled=false;});}}){IsBackground=true,Name="MiniAI desktop translation"}.Start();
        }
        void Post(int serial,Action action){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke(new Action(delegate{if(!IsDisposed&&serial==Volatile.Read(ref generation))action();}));}catch(InvalidOperationException){}}
        void ShowImage(Bitmap image,Rectangle area){if(bubble!=null&&!bubble.IsDisposed)bubble.Close();bubble=new TranslationBubble(backend,area,true);bubble.Show(this);bubble.Recognize(image);}
        void CaptureScreen(){bool hide=ContainsFocus;try{if(hide)Hide();Rectangle area;Bitmap image=CaptureOverlay.Pick(null,out area);if(hide)Show();if(image!=null)ShowImage(image,area);}catch(Exception error){if(hide)Show();status.Text=error.Message;}}
        void OpenImage(){using(var dialog=new OpenFileDialog{Filter="Photos and scans / 照片与扫描件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif",RestoreDirectory=true})if(dialog.ShowDialog(this)==DialogResult.OK)LoadPhoto(dialog.FileName);}
        internal void LoadPhoto(string path){try{using(var image=Image.FromFile(path))RecognizePhoto(image,Path.GetFileName(path));}catch(Exception error){status.Text=error.Message;}}
        void EnableDrop(Control control){control.AllowDrop=true;control.DragEnter+=delegate(object sender,DragEventArgs e){e.Effect=e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.Bitmap)?DragDropEffects.Copy:DragDropEffects.None;};control.DragDrop+=delegate(object sender,DragEventArgs e){var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&files.Length>0){LoadPhoto(files[0]);return;}var image=e.Data.GetData(DataFormats.Bitmap) as Image;if(image!=null)RecognizePhoto(image,"Dropped image / 拖入图片");};foreach(Control child in control.Controls)EnableDrop(child);}
        internal void PasteInput(){try{if(Clipboard.ContainsImage()){using(var image=Clipboard.GetImage())RecognizePhoto(image,"Clipboard / 剪贴板");}else if(Clipboard.ContainsText())input.Text=Clipboard.GetText();}catch(Exception error){status.Text=error.Message;}}
        void CaptureOcr(){bool hide=ContainsFocus;try{if(hide)Hide();Rectangle area;using(var image=CaptureOverlay.Pick(null,out area)){if(hide)Show();if(image!=null)RecognizePhoto(image,"Screenshot / 截图");}}catch(Exception error){if(hide)Show();status.Text=error.Message;}}
        internal static byte[] PhotoBytes(Image original) {
            Guard.Check(original.Width>0&&original.Height>0&&(long)original.Width*original.Height<=100000000,"Photo exceeds 100 megapixels / 照片过大。");
            double scale=Math.Min(1,Math.Min(4096.0/Math.Max(original.Width,original.Height),Math.Sqrt(16000000.0/((long)original.Width*original.Height))));
            using(var image=new Bitmap(Math.Max(1,(int)Math.Round(original.Width*scale)),Math.Max(1,(int)Math.Round(original.Height*scale)),PixelFormat.Format24bppRgb)) {
                using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.White);graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(original,new Rectangle(Point.Empty,image.Size));}
                if(Array.IndexOf(original.PropertyIdList,0x112)>=0){int orientation=BitConverter.ToUInt16(original.GetPropertyItem(0x112).Value,0);RotateFlipType rotation=RotateFlipType.RotateNoneFlipNone;switch(orientation){case 2:rotation=RotateFlipType.RotateNoneFlipX;break;case 3:rotation=RotateFlipType.Rotate180FlipNone;break;case 4:rotation=RotateFlipType.Rotate180FlipX;break;case 5:rotation=RotateFlipType.Rotate90FlipX;break;case 6:rotation=RotateFlipType.Rotate90FlipNone;break;case 7:rotation=RotateFlipType.Rotate270FlipX;break;case 8:rotation=RotateFlipType.Rotate270FlipNone;break;}image.RotateFlip(rotation);}
                using(var stream=new MemoryStream()){image.Save(stream,ImageFormat.Png);if(stream.Length<=17*1024*1024)return stream.ToArray();}
                var encoder=ImageCodecInfo.GetImageEncoders().First(x=>x.FormatID==ImageFormat.Jpeg.Guid);
                foreach(long quality in new long[]{92,80,65})using(var stream=new MemoryStream())using(var parameters=new EncoderParameters(1)){parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,quality);image.Save(stream,encoder,parameters);if(stream.Length<=17*1024*1024)return stream.ToArray();}
                throw new IOException("Photo is too complex; crop a smaller region / 请截取较小的照片区域。");
            }
        }
        void RecognizePhoto(Image image,string label) {
            byte[] bytes=PhotoBytes(image);Cancel();int serial=Volatile.Read(ref generation);translate.Enabled=false;cancel.Enabled=true;status.Text="Recognizing / 正在识别："+label;
            new Thread(delegate(){try{string text=Recognize(bytes,delegate{return serial!=Volatile.Read(ref generation);});Post(serial,delegate{input.Text=text;output.Clear();translate.Enabled=true;cancel.Enabled=false;status.Text=String.IsNullOrWhiteSpace(text)?"No text found / 未识别出文字，请换图或重新框选。":"OCR ready: edit or select the text above / 已识别，可在上方编辑、选择、复制或翻译。";if(autoTranslate.Checked&&!String.IsNullOrWhiteSpace(text))StartTranslation();});}catch(Exception error){Post(serial,delegate{status.Text=error.Message;translate.Enabled=true;cancel.Enabled=false;});}}){IsBackground=true,Name="MiniAI photo OCR"}.Start();
        }
        void SaveText(){using(var dialog=new SaveFileDialog{Filter="UTF-8 text / 文本|*.txt",FileName="OCR.txt",DefaultExt="txt",AddExtension=true,RestoreDirectory=true})if(dialog.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(dialog.FileName,input.Text,new System.Text.UTF8Encoding(false));}catch(Exception error){status.Text=error.Message;}}
        protected override void Dispose(bool disposing){if(disposing){Cancel();if(hotkey!=null)hotkey.Dispose();if(bubble!=null)bubble.Dispose();}base.Dispose(disposing);if(disposing&&applicationIcon!=null)applicationIcon.Dispose();}
    }
}
