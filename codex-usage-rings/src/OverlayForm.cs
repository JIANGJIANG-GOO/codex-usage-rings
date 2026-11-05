using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsageRings
{
    public sealed class OverlayForm : Form
    {
        readonly Timer hoverTimer = new Timer { Interval = 75 };
        QuotaTooltipForm tip;
        ResetCardsForm cards;
        IResetCardService resetService;
        bool ringPressed;
        public Action ResetFinished;
        UsageSnapshot resetBefore;
        readonly ResetRefill refill=new ResetRefill();
        readonly System.Diagnostics.Stopwatch animationClock=System.Diagnostics.Stopwatch.StartNew();
        public bool CardsVisible { get { return cards!=null&&!cards.IsDisposed&&cards.Presented; } }
        string hoveredRing;
        DateTime hoverStartedUtc;
        DateTime hoverRetryUtc;
        public bool TooltipVisible { get { return tip != null && !tip.IsDisposed && tip.Presented; } }
        public int TooltipShowCount { get; private set; }
        public string HoverTarget { get { return hoveredRing; } }
        public int TooltipErrorCount { get; private set; }
        public string TooltipErrorType { get; private set; }
        IntPtr hostWindow;
        public bool Embedded { get { return IsHandleCreated && hostWindow!=IntPtr.Zero && GetParent(Handle)==hostWindow; } }
        public void BindToHost(IntPtr host) {
            if(host==IntPtr.Zero)throw new ArgumentException("Missing host");
            if(hostWindow==host && IsHandleCreated && GetParent(Handle)==host)return;
            if(cards!=null)cards.HidePanel();
            hostWindow=host;Hide();
            if(IsHandleCreated)RecreateHandle();else CreateHandle();
            if(GetParent(Handle)!=host)throw new InvalidOperationException("Native child attachment failed");
        }
        RingLayout layout;
        UsageSnapshot snapshot;
        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopLevel = false;
            StartPosition = FormStartPosition.Manual; Text = "Codex Usage Rings";
            // A native child has no managed TopLevelControl, so WinForms ToolTip cannot
            // register a tooltip owner. Sampling the cursor also covers transparent
            // circle interiors, where layered windows receive no mouse messages.
            hoverTimer.Tick += delegate {
                if(DateTime.UtcNow<hoverRetryUtc)return;
                try { PollHover(); }
                catch(ExternalException error) { HoverFailed(error); }
                catch(InvalidOperationException error) { HoverFailed(error); }
            };
            hoverTimer.Start();
            VisibleChanged += delegate { if(!Visible) { HideTooltip();if(cards!=null)cards.HidePanel(); } };
        }
        internal OverlayForm(IResetCardService service):this(){resetService=service;}
        bool RingHit(Point p) { return layout!=null&&(Hit(layout.Week,p,layout.Scale)||(RingVisual.From(snapshot,snapshot==null?null:snapshot.FiveHour,DateTime.UtcNow).Known&&Hit(layout.FiveHour,p,layout.Scale))); }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);ringPressed=e.Button==MouseButtons.Left&&RingHit(e.Location);}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);bool click=ringPressed&&e.Button==MouseButtons.Left&&RingHit(e.Location);ringPressed=false;if(click)ToggleCards();}
        internal void PrepareCards(){
            if(cards!=null&&!cards.IsDisposed)return;
            if(resetService==null)resetService=new ResetCardService();
            cards=new ResetCardsForm(resetService);cards.Started=delegate{resetBefore=snapshot;};cards.Finished=delegate(ResetResult result){if(result.Success)refill.Arm(resetBefore??snapshot,DateTime.UtcNow,animationClock.ElapsedMilliseconds);resetBefore=null;if(ResetFinished!=null)ResetFinished();};
            cards.Warmup();
        }
        internal void ToggleCards(){
            if(CardsVisible){cards.HidePanel();return;}
            if(layout==null||hostWindow==IntPtr.Zero)return;
            HideTooltip();
            PrepareCards();
            cards.Present(hostWindow,PointToScreen(new Point(Width+8,0)),layout.Scale);
        }
        void PollHover()
        {
            if(CardsVisible){HideTooltip();return;}
            POINT pointer=new POINT();
            bool available = IsHandleCreated && !IsDisposed && Visible && hostWindow != IntPtr.Zero &&
                IsWindowVisible(Handle) && WindowLocator.IsForeground(hostWindow) && GetCursorPos(out pointer);
            if(!available) { HideTooltip(); return; }
            IntPtr pointedWindow=WindowFromPoint(pointer);
            ApplyHoverSample(new Point(pointer.X,pointer.Y),pointedWindow,DateTime.UtcNow);
        }
        internal void ApplyHoverSample(Point screenPointer,IntPtr pointedWindow,DateTime now)
        {
            bool unobscured=pointedWindow!=IntPtr.Zero && GetAncestor(pointedWindow,2)==hostWindow;
            UpdateHover(screenPointer,unobscured,now);
        }
        void HoverFailed(Exception error)
        {
            TooltipErrorCount++;TooltipErrorType=error.GetType().Name;
            hoverRetryUtc=DateTime.UtcNow.AddSeconds(5);
            try { HideTooltip(); }
            catch(ExternalException) { hoveredRing=null; }
            catch(InvalidOperationException) { hoveredRing=null; }
        }
        internal void UpdateHover(Point screenPointer,bool allowed,DateTime now)
        {
            if(!allowed || !IsHandleCreated || !Visible || layout==null) { HideTooltip(); return; }
            Point p=PointToClient(screenPointer);
            bool week=Hit(layout.Week,p,layout.Scale);
            bool five=!week && RingVisual.From(snapshot,snapshot==null?null:snapshot.FiveHour,now).Known && Hit(layout.FiveHour,p,layout.Scale);
            string target=week?"7d":five?"5h":null;
            if(target==null) { HideTooltip(); return; }
            if(target!=hoveredRing || now<hoverStartedUtc) {
                HideTooltip();hoveredRing=target;hoverStartedUtc=now;return;
            }
            if((now-hoverStartedUtc).TotalMilliseconds<350)return;
            string description=RingPainter.Describe(target,snapshot,snapshot==null?null:(week?snapshot.Week:snapshot.FiveHour),now);
            if(tip==null || tip.IsDisposed)tip=new QuotaTooltipForm();
            RectangleF ring=week?layout.Week:layout.FiveHour;
            Point position=PointToScreen(new Point((int)Math.Ceiling(ring.Right+10*layout.Scale),(int)Math.Floor(ring.Top)));
            Rectangle ownerArea=RectangleToScreen(ClientRectangle);
            bool wasVisible=tip.Presented;
            tip.Present(description,position,layout.Scale,ownerArea);
            if(!wasVisible)TooltipShowCount++;
        }
        void HideTooltip()
        {
            if(tip!=null && !tip.IsDisposed)tip.HidePopup();
            hoveredRing=null;
        }
        static bool Hit(RectangleF r,Point p,float scale) { double dx=p.X-r.Left-r.Width/2,dy=p.Y-r.Top-r.Height/2; double radius=r.Width/2+RingPainter.StrokeWidth*scale/2; return dx*dx+dy*dy<=radius*radius; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80000 | 0x08000000 | 0x80;
                p.Style=(p.Style & ~unchecked((int)0x80000000)) | 0x40000000;
                p.Parent=hostWindow;return p; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        protected override void OnHandleDestroyed(EventArgs e) { HideTooltip();if(cards!=null)cards.HidePanel();base.OnHandleDestroyed(e); }
        public void Render(RingLayout nextLayout, UsageSnapshot nextSnapshot, ModeState mode, bool allowMotion=true)
        {
            refill.Observe(nextSnapshot,DateTime.UtcNow,animationClock.ElapsedMilliseconds);
            layout = nextLayout; snapshot = nextSnapshot; Rectangle hostBounds=WindowLocator.GetHostBounds(hostWindow);
            if(hostBounds.IsEmpty) return;
            Rectangle relative=layout.Bounds;relative.Offset(-hostBounds.Left,-hostBounds.Top);
            Bounds=relative;
            SetWindowPos(Handle,IntPtr.Zero,relative.X,relative.Y,relative.Width,relative.Height,0x10);
            using (Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) {
                    g.Clear(Color.Transparent);
                    RingPainter.Draw(g, layout, snapshot, DateTime.UtcNow, mode, allowMotion,refill.Sample(false,animationClock.ElapsedMilliseconds),refill.Sample(true,animationClock.ElapsedMilliseconds));
                }
                SetBitmap(bitmap);
            }
            if(CardsVisible)cards.MoveNear(PointToScreen(new Point(Width+8,0)));
        }
        void SetBitmap(Bitmap bitmap)
        {
            IntPtr screen=IntPtr.Zero,memory=IntPtr.Zero,handle=IntPtr.Zero,old=IntPtr.Zero;
            try {
                screen=GetDC(IntPtr.Zero);
                if(screen==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
                memory=CreateCompatibleDC(screen);
                if(memory==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
                handle=bitmap.GetHbitmap(Color.FromArgb(0));
                old=SelectObject(memory,handle);
                if(old==IntPtr.Zero||old==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();
                POINT source=new POINT(0,0);SIZE size=new SIZE(Width,Height);
                BLENDFUNCTION blend=new BLENDFUNCTION{BlendOp=0,SourceConstantAlpha=255,AlphaFormat=1};
                if(!UpdateLayeredWindow(Handle,screen,IntPtr.Zero,ref size,memory,ref source,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            } finally {
                if(old!=IntPtr.Zero&&old!=new IntPtr(-1))SelectObject(memory,old);
                if(handle!=IntPtr.Zero)DeleteObject(handle);
                if(memory!=IntPtr.Zero)DeleteDC(memory);
                if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);
            }
        }

        protected override void Dispose(bool disposing) {
            if(disposing) { hoverTimer.Stop();hoverTimer.Dispose();if(tip!=null)tip.Dispose();if(cards!=null)cards.Dispose();if(resetService!=null)resetService.Dispose(); }
            base.Dispose(disposing);
        }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X,Y; public POINT(int x,int y) { X=x;Y=y; } }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int X,Y; public SIZE(int x,int y) { X=x;Y=y; } }
        [StructLayout(LayoutKind.Sequential, Pack=1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr handle);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr handle, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll", SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dc, IntPtr destination, ref SIZE size, IntPtr sourceDc, ref POINT source, int colorKey, ref BLENDFUNCTION blend, int flags);
    }

    // A separate popup is required because the embedded ring has a foreign native
    // parent. It never activates, captures input, or depends on a managed parent.
    internal sealed class QuotaTooltipForm : Form
    {
        string description="";
        float scale;
        int padding;
        public bool Presented { get { return IsHandleCreated && IsWindowVisible(Handle); } }
        const TextFormatFlags TextFlags=TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
        public QuotaTooltipForm()
        {
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
            StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(38,38,41);
            ForeColor=Color.FromArgb(242,242,245);AccessibleRole=AccessibleRole.ToolTip;
            Text="Codex quota details";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get { CreateParams p=base.CreateParams;p.ExStyle|=0x08000000|0x80|0x20;return p; }
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x21) { message.Result=new IntPtr(3);return; }
            if(message.Msg==0x84) { message.Result=new IntPtr(-1);return; }
            base.WndProc(ref message);
        }
        public void Present(string value,Point position,float nextScale,Rectangle ownerArea)
        {
            if(scale!=nextScale) {
                Font old=Font;Font=new Font("Segoe UI",12f*nextScale,FontStyle.Regular,GraphicsUnit.Pixel);
                if(scale>0)old.Dispose();scale=nextScale;padding=(int)Math.Ceiling(9*scale);
            }
            if(description!=value) { description=value;AccessibleDescription=value;Invalidate(); }
            Size measured=TextRenderer.MeasureText(description,Font,Size.Empty,TextFlags);
            Size size=new Size(measured.Width+padding*2,measured.Height+padding*2);
            Rectangle area=Screen.FromRectangle(ownerArea).WorkingArea;
            // Keep an off-screen diagnostic/host off-screen rather than jumping a
            // tooltip onto an unrelated monitor.
            if(area.IntersectsWith(ownerArea)) {
                position.X=Math.Max(area.Left,Math.Min(position.X,area.Right-size.Width));
                position.Y=Math.Max(area.Top,Math.Min(position.Y,area.Bottom-size.Height));
            }
            if(!IsHandleCreated)Bounds=new Rectangle(position,size);
            // The managed Form.Show path activated this popup with a foreign parent,
            // despite ShowWithoutActivation, making the next hover poll hide it.
            // Native show/position bypasses the managed visibility/activation path.
            if(!SetWindowPos(Handle,new IntPtr(-1),position.X,position.Y,size.Width,size.Height,0x0010|0x0040))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public void HidePopup() { if(IsHandleCreated)ShowWindow(Handle,0); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using(Pen border=new Pen(Color.FromArgb(74,74,80)))e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
            TextRenderer.DrawText(e.Graphics,description,Font,new Point(padding,padding),ForeColor,TextFlags);
        }
        protected override void Dispose(bool disposing) { if(disposing && scale>0)Font.Dispose();base.Dispose(disposing); }
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    }
}
