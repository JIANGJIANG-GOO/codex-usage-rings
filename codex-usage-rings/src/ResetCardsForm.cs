using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;
namespace CodexUsageRings {
 internal sealed class ResetCardsForm : Form {
  readonly IResetCardService service;readonly ResetConfirmation confirmation=new ResetConfirmation();
  readonly Stopwatch clock=Stopwatch.StartNew();readonly Timer timer=new Timer{Interval=50};
  DateTime loadedAtUtc=DateTime.MinValue;
  ResetCardList list;string message="";bool loading,disposed;int generation,scroll;IntPtr host;
  Point anchorPoint;Point down;bool hasDown;float scale=1;Rectangle refresh;Rectangle[] buttons=new Rectangle[0];
  internal Action Started;internal Action<ResetResult> Finished;internal bool Presented{get{return IsHandleCreated&&IsWindowVisible(Handle);}}
  internal ResetCardList CurrentList{get{return list;}}
  internal bool Busy{get{return confirmation.Busy;}}
  internal string Selected{get{return confirmation.Selected;}}
  internal ResetCardsForm(IResetCardService source){
   service=source;Text="Codex 重置卡";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
   BackColor=Color.FromArgb(30,30,33);ForeColor=Color.FromArgb(234,234,239);
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
   timer.Tick+=delegate{if(!Presented)return;try{
    if(!WindowLocator.IsForeground(host)){HidePanel();return;}
    if(loading)Invalidate(refresh);
    string before=confirmation.Selected;confirmation.Expire(clock.ElapsedMilliseconds);if(before!=confirmation.Selected)Invalidate();
    // Read the transition bit only for dismissal, never for submitting a reset.
    if((GetAsyncKeyState(1)&1)!=0&&!RectangleToScreen(ClientRectangle).Contains(Cursor.Position))HidePanel();
   }catch(ExternalException){HidePanel();}catch(InvalidOperationException){HidePanel();}};
  }
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
  protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
  internal void Present(IntPtr owner,Point point,float dpi){
   host=owner;scale=dpi;Font old=Font;Font=new Font("Segoe UI",12*scale,FontStyle.Regular,GraphicsUnit.Pixel);if(old!=Control.DefaultFont)old.Dispose();
   ClientSize=new Size((int)(320*scale),(int)(100*scale));MoveNear(point);
   GetAsyncKeyState(1);timer.Start();if(list==null||list.Error!=null||(DateTime.UtcNow-loadedAtUtc).TotalSeconds>=60)LoadCards();
  }
  internal void Warmup(){var unused=Handle;LoadCards();}
  internal void MoveNear(Point point){
   anchorPoint=point;
   if(host==IntPtr.Zero)return;Rectangle hostBounds=WindowLocator.GetHostBounds(host);if(hostBounds.IsEmpty){HidePanel();return;}
   Rectangle work=Screen.FromRectangle(hostBounds).WorkingArea;
   int rows=list==null?0:Math.Min(6,list.Cards.Count);
   int height=Math.Min((int)((42+rows*52+FooterHeight)*scale),Math.Max(160,work.Height-16));int width=Math.Min((int)(320*scale),Math.Max(200,work.Width-16));
   point=PositionPanel(point,hostBounds,work,new Size(width,height));
   if(!SetWindowPos(Handle,new IntPtr(-1),point.X,point.Y,width,height,0x0010|0x0040))throw new System.ComponentModel.Win32Exception();
   LayoutButtons();
  }
  internal static Point PositionPanel(Point anchor,Rectangle hostBounds,Rectangle work,Size size){
   // Keep the same bottom-aligned attachment in maximized and restored windows.
   // Monitor clamping is only a final visibility safeguard, not the anchor.
   var point=new Point(anchor.X,hostBounds.Bottom-size.Height);
   if(work.IntersectsWith(hostBounds)){point.X=Math.Max(work.Left,Math.Min(point.X,work.Right-size.Width));point.Y=Math.Max(work.Top,Math.Min(point.Y,work.Bottom-size.Height));}
   return point;
  }
  internal void HidePanel(){timer.Stop();confirmation.Cancel();hasDown=false;if(IsHandleCreated)ShowWindow(Handle,0);}
  void Post(Action action){if(disposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{if(!disposed)action();});}catch(InvalidOperationException){} }
  internal void LoadCards(){
   if(loading||confirmation.Busy)return;confirmation.Cancel();loading=true;message="";int read=++generation;if(Presented)MoveNear(anchorPoint);Invalidate();
   Task.Factory.StartNew(delegate{ResetCardList result;try{result=service.Read();}catch{result=new ResetCardList{Error="读取失败，请刷新重试。"};}
    Post(delegate{if(read!=generation)return;loading=false;list=result;loadedAtUtc=result.Error==null?DateTime.UtcNow:DateTime.MinValue;scroll=0;message=result.Error??(!result.DetailsKnown?"仅返回卡片数量，暂时无法选择卡片。":result.Cards.Count==0?"暂无可用重置卡。":"");if(Presented)MoveNear(anchorPoint);LayoutButtons();Invalidate();});});
  }
  float FooterHeight{get{return (String.IsNullOrEmpty(message)?4:48)+(list!=null&&list.Cards.Count>6?20:0);}}
  int PageSize{get{return Math.Max(1,(int)((ClientSize.Height/scale-42-FooterHeight+0.5f)/52));}}
  void LayoutButtons(){int count=list==null?0:Math.Min(PageSize,Math.Max(0,list.Cards.Count-scroll));buttons=new Rectangle[count];
   for(int i=0;i<count;i++)buttons[i]=new Rectangle(ClientSize.Width-(int)(80*scale),(int)((48+i*52)*scale),(int)(68*scale),(int)(30*scale));
   refresh=new Rectangle(ClientSize.Width-(int)(80*scale),(int)(6*scale),(int)(68*scale),(int)(30*scale));
   AccessibleDescription="重置卡；"+(list==null?"正在读取":list.AvailableCount+" 张可用；"+message);
  }
  internal void ClickCard(int index,long milliseconds){
   if(loading||confirmation.Busy||list==null||index<0||index>=list.Cards.Count)return;
   ResetCard card=list.Cards[index];if(!card.Available(DateTime.UtcNow)||String.IsNullOrWhiteSpace(list.AccountId))return;
   if(!confirmation.Click(card.Id,milliseconds)){Invalidate();return;}
   if(Started!=null)Started();
   message="正在重置，请稍候…";ResetCardList shown=list;if(Presented)MoveNear(anchorPoint);Invalidate();
   Task.Factory.StartNew(delegate{ResetResult result;ResetCardList updated;
    try{result=service.Consume(shown,card.Id);}catch{result=new ResetResult{Outcome="error",Message="未确认结果，请检查额度后再操作。"};}
    try{updated=service.Read();}catch{updated=new ResetCardList{Error="刷新失败，请稍后重试。"};}
    Post(delegate{confirmation.Complete();list=updated;loadedAtUtc=updated.Error==null?DateTime.UtcNow:DateTime.MinValue;scroll=0;message=result.Message;if(Presented)MoveNear(anchorPoint);LayoutButtons();Invalidate();if(Finished!=null)Finished(result);});});
  }
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){down=e.Location;hasDown=true;}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!hasDown)return;hasDown=false;
   if(refresh.Contains(down)&&refresh.Contains(e.Location)){LoadCards();return;}
   for(int i=0;i<buttons.Length;i++)if(buttons[i].Contains(down)&&buttons[i].Contains(e.Location)){ClickCard(scroll+i,clock.ElapsedMilliseconds);return;}
   HidePanel();
  }
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(list==null||confirmation.Busy)return;confirmation.Cancel();scroll=Math.Max(0,Math.Min(Math.Max(0,list.Cards.Count-PageSize),scroll+(e.Delta<0?1:-1)));LayoutButtons();Invalidate();}
  void TextAt(Graphics g,string text,Rectangle bounds,Color color){TextRenderer.DrawText(g,text,Font,bounds,color,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
  Rectangle Box(float x,float y,float w,float h){return new Rectangle((int)(x*scale),(int)(y*scale),(int)(w*scale),(int)(h*scale));}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.Clear(BackColor);
   using(var pen=new Pen(Color.FromArgb(72,72,78)))g.DrawRectangle(pen,0,0,Width-1,Height-1);
   TextAt(g,"重置卡"+(list==null?"":" · "+list.AvailableCount+" 张可用"),Box(12,12,220,24),ForeColor);
   for(int i=0;i<buttons.Length;i++){var card=list.Cards[scroll+i];int y=42+i*52;
    TextAt(g,card.Title,Box(12,y,216,22),ForeColor);
    string expiry=card.ExpiresAtUtc.HasValue?card.ExpiresAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"无到期时间";
    TextAt(g,"到期："+expiry,Box(12,y+22,216,20),Color.FromArgb(170,170,181));
    bool available=card.Available(DateTime.UtcNow)&&!String.IsNullOrWhiteSpace(list.AccountId);
    bool enabled=!loading&&!confirmation.Busy&&available;
    using(var brush=new SolidBrush(confirmation.Selected==card.Id?Color.FromArgb(90,65,125):Color.FromArgb(52,52,58)))g.FillRectangle(brush,buttons[i]);
    string label=confirmation.Busy?"处理中":!available?"不可用":confirmation.Selected==card.Id?"确认":"重置";
    TextRenderer.DrawText(g,label,Font,buttons[i],enabled?ForeColor:Color.Gray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
   }
   if(!String.IsNullOrEmpty(message))TextAt(g,message,new Rectangle((int)(16*scale),Height-(int)(44*scale),Width-(int)(32*scale),(int)(40*scale)),Color.FromArgb(185,185,195));
   using(var fill=new SolidBrush(Color.FromArgb(52,52,58)))g.FillRectangle(fill,refresh);
   if(loading)DrawSpinner(g,refresh,scale,clock.ElapsedMilliseconds);
   else TextRenderer.DrawText(g,"刷新",Font,refresh,confirmation.Busy?Color.Gray:ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
   if(list!=null&&list.Cards.Count>PageSize)TextAt(g,"滚轮查看更多",new Rectangle((int)(12*scale),Height-(int)(22*scale),Width-(int)(24*scale),(int)(20*scale)),Color.Gray);
  }
  internal static void DrawSpinner(Graphics g,Rectangle bounds,float scale,long elapsed){
   var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
   float cx=bounds.Left+bounds.Width/2f,cy=bounds.Top+bounds.Height/2f;int phase=(int)((elapsed/80)%12);
   for(int i=0;i<12;i++){double a=(i*30-90)*Math.PI/180;int age=(phase-i+12)%12;
    using(var pen=new Pen(Color.FromArgb(245-age*17,235,235,240),2.1f*scale)){
     pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
     g.DrawLine(pen,cx+(float)Math.Cos(a)*5.8f*scale,cy+(float)Math.Sin(a)*5.8f*scale,cx+(float)Math.Cos(a)*9.3f*scale,cy+(float)Math.Sin(a)*9.3f*scale);
    }
   }g.Restore(state);
  }
  protected override void Dispose(bool disposing){disposed=true;if(disposing){timer.Dispose();Font.Dispose();}base.Dispose(disposing);}
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int command);
  [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 }
}
