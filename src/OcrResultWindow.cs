using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MiniAI.Desktop {
    // One editable source drives preview, MathML and all clipboard formats.
    internal sealed class OcrResultWindow : Form {
        readonly Bitmap image;
        Bitmap rendered;
        readonly string resultsFolder;
        readonly Func<string,string> label;
        readonly PictureBox preview,original;
        readonly TextBox code;
        readonly Label state;
        readonly ComboBox contents,format;
        readonly TabControl tabs;
        readonly Button copy,save;
        readonly ProgressBar progress;
        readonly System.Windows.Forms.Timer debounce;
        readonly List<string> snippets=new List<string>();
        bool updating,hasFormula;
        int current=-1;
        string mathml="";
        internal OcrResultWindow(Bitmap source,string folder,Func<string,string> text) {
            image=new Bitmap(source);resultsFolder=folder;label=text;Text=text("截图识别与公式");Font=new Font("Segoe UI",10);ClientSize=new Size(840,640);MinimumSize=new Size(620,460);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(244,246,249);AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=4};layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,54));layout.RowStyles.Add(new RowStyle(SizeType.Percent,46));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
            var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,AutoSize=true,Margin=new Padding(0,0,0,8)};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));contents=new ComboBox{Name="OcrContents",Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Visible=false};contents.SelectedIndexChanged+=delegate{if(updating||contents.SelectedIndex<0)return;if(current>=0&&current<snippets.Count)snippets[current]=code.Text;current=contents.SelectedIndex;hasFormula=current>0;code.WordWrap=!hasFormula;code.Text=snippets[current];UpdateFormats();UpdatePreview();};header.Controls.Add(contents,0,0);state=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Height=28,TextAlign=ContentAlignment.MiddleRight,ForeColor=Color.FromArgb(37,99,235),Text=text("正在识别…")};header.Controls.Add(state,1,0);layout.Controls.Add(header,0,0);
            tabs=new TabControl{Dock=DockStyle.Fill};var mathTab=new TabPage(text("公式预览")){BackColor=Color.White};var originalTab=new TabPage(text("原图")){BackColor=Color.White};preview=new PictureBox{Name="FormulaPreview",Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White};original=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Image=image,BackColor=Color.White};mathTab.Controls.Add(preview);originalTab.Controls.Add(original);tabs.TabPages.Add(mathTab);tabs.TabPages.Add(originalTab);tabs.SelectedIndex=1;layout.Controls.Add(tabs,0,1);
            code=new TextBox{Name="ScreenshotTex",Multiline=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",11),ScrollBars=ScrollBars.Both,WordWrap=false,AcceptsReturn=true,AcceptsTab=true,BackColor=Color.White,MaxLength=20000,Margin=new Padding(0,8,0,8)};layout.Controls.Add(code,0,2);
            var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));format=new ComboBox{Name="FormulaCopyFormat",Width=230,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,4,8,0)};format.SelectedIndexChanged+=delegate{copy.Enabled=code.TextLength>0&&(format.SelectedIndex!=0||!hasFormula||mathml.Length>0);};var left=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};left.Controls.Add(format);copy=Button(text("复制"),CopyCode);copy.Name="CopyFormula";copy.BackColor=Color.FromArgb(37,99,235);copy.ForeColor=Color.White;left.Controls.Add(copy);save=Button(text("保存…"),SaveCode);left.Controls.Add(save);footer.Controls.Add(left,0,0);footer.Controls.Add(Button(text("关闭"),Close),1,0);layout.Controls.Add(footer,0,3);progress=new ProgressBar{Dock=DockStyle.Top,Height=3,Style=ProgressBarStyle.Marquee};Controls.Add(progress);copy.Enabled=save.Enabled=format.Enabled=false;code.Enabled=false;
            debounce=new System.Windows.Forms.Timer{Interval=180};debounce.Tick+=delegate{debounce.Stop();UpdatePreview();};code.TextChanged+=delegate{if(!updating){debounce.Stop();debounce.Start();}};
        }
        Button Button(string title,Action action){var button=new Button{Text=title,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(70,32),FlatStyle=FlatStyle.Flat,Margin=new Padding(4,0,0,0)};button.FlatAppearance.BorderColor=Color.FromArgb(210,218,230);button.Click+=delegate{try{action();}catch(Exception error){state.Text=error.Message;}};return button;}
        internal void SetRecognizing(){if(IsDisposed)return;state.Text=label("正在识别…");progress.Visible=true;}
        internal void SetResult(string value,bool formula){SetResult(value,formula?new List<string>{value}:new List<string>());}
        internal void SetResult(string value,List<string> formulas){if(IsDisposed)return;updating=true;snippets.Clear();snippets.Add(value??"");contents.Items.Clear();contents.Items.Add(label("全部识别文字"));for(int i=0;i<formulas.Count;i++){snippets.Add(formulas[i]);contents.Items.Add(label("公式")+" "+(i+1));}current=formulas.Count>0?1:0;contents.SelectedIndex=current;contents.Visible=formulas.Count>0;hasFormula=current>0;code.WordWrap=!hasFormula;code.Text=snippets[current];code.Select(0,0);code.Enabled=format.Enabled=save.Enabled=true;progress.Visible=false;UpdateFormats();updating=false;UpdatePreview();}
        void UpdateFormats(){int choice=format.SelectedIndex;format.Items.Clear();if(hasFormula)format.Items.AddRange(new[]{"MathType / MathML","LaTeX",label("行内 LaTeX"),label("独立 LaTeX"),"PNG"});else format.Items.Add(label("纯文字"));format.SelectedIndex=Math.Max(0,Math.Min(format.Items.Count-1,choice));}
        void UpdatePreview(){if(IsDisposed)return;mathml="";if(hasFormula&&code.TextLength>0)try{mathml=FormulaMarkup.Convert(code.Text);}catch(Exception error){state.Text=label("可复制 LaTeX；MathML 暂不支持此语法。")+" "+error.Message;}var old=rendered;rendered=null;preview.Image=null;if(hasFormula&&mathml.Length>0)try{rendered=FormulaPreview.Render(code.Text);preview.Image=rendered;tabs.SelectedIndex=0;state.Text=label("核对后复制到 MathType，或继续修改 LaTeX。");state.ForeColor=Color.FromArgb(28,120,65);}catch(Exception){tabs.SelectedIndex=1;state.Text=label("请核对原图；仍可复制 MathML 或 LaTeX。");}else if(!hasFormula){tabs.SelectedIndex=1;state.Text=String.IsNullOrWhiteSpace(code.Text)?label("未识别出内容"):label("识别完成");}if(old!=null)old.Dispose();copy.Enabled=code.TextLength>0&&(!hasFormula||format.SelectedIndex!=0||mathml.Length>0);save.Enabled=code.TextLength>0;}
        internal void SetError(string value){if(IsDisposed)return;progress.Visible=false;state.Text=value;state.ForeColor=Color.FromArgb(180,60,45);copy.Enabled=save.Enabled=false;}
        internal void CopyCode(){string value=code.SelectionLength>0?code.SelectedText:code.Text;if(value.Length==0)return;if(!hasFormula){Clipboard.SetText(value);return;}switch(format.SelectedIndex){case 0:FormulaMarkup.CopyMathML(value);break;case 2:Clipboard.SetText("$"+FormulaMarkup.Clean(value)+"$");break;case 3:Clipboard.SetText("$$"+FormulaMarkup.Clean(value)+"$$");break;case 4:if(rendered==null)throw new InvalidOperationException(label("公式预览暂不可用。"));Clipboard.SetImage(rendered);break;default:Clipboard.SetText(FormulaMarkup.Clean(value));break;}state.Text=label("已复制");}
        void SaveCode(){if(code.TextLength==0)return;Directory.CreateDirectory(resultsFolder);bool png=hasFormula&&format.SelectedIndex==4,xml=hasFormula&&format.SelectedIndex==0;string extension=png?"png":xml?"mml":hasFormula?"tex":"txt";using(var dialog=new SaveFileDialog{Filter=png?"PNG image|*.png":xml?"MathML|*.mml":hasFormula?"LaTeX|*.tex|UTF-8 text|*.txt":"UTF-8 text|*.txt",FileName="Screenshot."+extension,DefaultExt=extension,AddExtension=true,InitialDirectory=resultsFolder,RestoreDirectory=true})if(dialog.ShowDialog(this)==DialogResult.OK){if(png){if(rendered==null)throw new InvalidOperationException(label("公式预览暂不可用。"));rendered.Save(dialog.FileName,ImageFormat.Png);}else File.WriteAllText(dialog.FileName,xml?FormulaMarkup.Convert(code.Text):code.Text,new UTF8Encoding(false));}}
        protected override bool ProcessCmdKey(ref Message message,Keys keys){if(keys==Keys.Escape){Close();return true;}if(keys==(Keys.Control|Keys.Shift|Keys.C)){try{CopyCode();}catch(Exception error){state.Text=error.Message;}return true;}return base.ProcessCmdKey(ref message,keys);}
        protected override void Dispose(bool disposing){if(disposing){if(debounce!=null)debounce.Dispose();if(preview!=null)preview.Image=null;if(original!=null)original.Image=null;if(rendered!=null){rendered.Dispose();rendered=null;}image.Dispose();}base.Dispose(disposing);}
    }
}
