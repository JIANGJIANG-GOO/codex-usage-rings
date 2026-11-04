using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexUsageRings
{
    static class Program
    {
        public const string StopEventName = "Local\\CodexUsageRings.Stop.v1";
        public const string MutexName = "Local\\CodexUsageRings.Instance.v1";
        public static string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageRings");
        [STAThread]
        static int Main(string[] args)
        {
            EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                if (args.Length > 1 && args[0] == "--preview") { Preview(args[1]); return 0; }
                if (args.Length > 1 && args[0] == "--diagnose") {
                    AnchorResult anchor = new WindowLocator().FindAnchor(true);
                    UsageSnapshot usage = new UsageProvider().Fetch();
                    WriteJson(args[1], Status(anchor, usage, false)); return 0;
                }
                if (args.Length > 0 && args[0] == "--stop") {
                    EventWaitHandle existing;
                    if (EventWaitHandle.TryOpenExisting(StopEventName, out existing)) using(existing) existing.Set();
                    return 0;
                }
                if (args.Length > 1 && args[0] == "--data-dir") DataDirectory = Path.GetFullPath(args[1]);
                bool created;
                using (Mutex singleton = new Mutex(true, MutexName, out created)) {
                    if (!created) return 0;
                    Directory.CreateDirectory(DataDirectory);
                    using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName)) {
                        stop.Reset();
                        using (OverlayContext context = new OverlayContext(stop)) Application.Run(context);
                    }
                    singleton.ReleaseMutex();
                }
                return 0;
            } catch (Exception error) {
                try { Directory.CreateDirectory(DataDirectory); WriteJson(Path.Combine(DataDirectory,"error.json"),new { errorType=error.GetType().Name, at=DateTime.UtcNow.ToString("o") }); } catch { }
                return 1;
            }
        }
        public static object Status(AnchorResult anchor, UsageSnapshot usage, bool visible, ModeState mode = null, RenderGuard renderGuard = null, bool embedded = false, object hover = null)
        {
            DateTime now = DateTime.UtcNow;
            return new {
                version = "0.3.2", checkedAtUtc = now.ToString("o"), pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                effects = new { purple = visible && mode!=null && mode.ModelFresh(now) && mode.Ultra, fastFlow = visible && mode!=null && mode.Fresh(now) && mode.Fast && (renderGuard==null||renderGuard.MotionAllowed(now)) },
                presentation = "native-child", embedded = embedded, hover = hover,
                rendering = renderGuard,
                displayState = DisplayState(visible,mode,renderGuard,now),
                mode = mode, innerDiameter = anchor==null?0:anchor.Avatar.Width, ringWidth = anchor==null?0:4*anchor.Scale,
                overlayVisible = visible, anchorFound = anchor != null && anchor.Found,
                anchorReason = anchor == null ? "正在查找 Codex 窗口" : anchor.Reason,
                avatar = anchor == null || !anchor.Found ? null : new { x=anchor.Avatar.X,y=anchor.Avatar.Y,width=anchor.Avatar.Width,height=anchor.Avatar.Height },
                dpiScale = anchor == null ? 0 : anchor.Scale,
                source = usage == null ? "unavailable" : usage.Source,
                sampledAtUtc = usage == null ? null : usage.CapturedAtUtc.ToString("o"),
                week = WindowStatus(usage,usage==null?null:usage.Week,now),
                fiveHour = WindowStatus(usage,usage==null?null:usage.FiveHour,now)
            };
        }
        public static string DisplayState(bool visible,ModeState mode,RenderGuard guard,DateTime now)
        {
            if(!visible)return "overlay-hidden";
            if(mode==null||!mode.Fresh(now))return "mode-not-ready";
            if(!mode.FastKnown)return "fast-unknown";
            if(!mode.Fast)return "fast-off";
            if(guard!=null&&!guard.MotionAllowed(now))return "animation-cooldown";
            return "fast-flow-active";
        }
        static object WindowStatus(UsageSnapshot snapshot, QuotaWindow quota, DateTime now)
        {
            RingVisual visual=RingVisual.From(snapshot,quota,now);
            return new { available=visual.Known, remainingPercent=visual.Known?(double?)visual.Remaining:null,
                usedPercent=quota==null?(double?)null:quota.UsedPercent,
                resetsAtUtc=quota==null||!quota.ResetsAtUtc.HasValue?null:quota.ResetsAtUtc.Value.ToString("o"),
                state=visual.Known?(visual.Remaining>15?"green":"red"):"unavailable", reason=visual.Reason };
        }
        public static void WriteJson(string path, object value)
        {
            string full=Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp=full+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value),new UTF8Encoding(false));
            if(File.Exists(full)) File.Replace(temp,full,null); else File.Move(temp,full);
        }
        static void Preview(string path)
        {
            using(Bitmap image=new Bitmap(800,300,PixelFormat.Format32bppArgb))
            using(Graphics g=Graphics.FromImage(image))
            using(Font title=new Font("Segoe UI",15,FontStyle.Bold))
            using(Font caption=new Font("Segoe UI",10))
            using(Brush ink=new SolidBrush(Color.FromArgb(230,232,236)))
            {
                g.Clear(Color.FromArgb(18,18,20));
                g.DrawString("Codex Usage Rings",title,ink,22,15);
                double[] remaining={72,12,72,72};
                string[] captions={"72% remaining","12% remaining","Astra Ultra + Fast","5h unavailable"};
                for(int i=0;i<4;i++) {
                    RingLayout layout=RingLayout.FromAvatar(new Rectangle(0,0,36,36));
                    var state=g.Save(); g.TranslateTransform(70+i*195,65);
                    UsageSnapshot usage=new UsageSnapshot{CapturedAtUtc=DateTime.UtcNow,Source="preview"};
                    if(i<4) { usage.Week=new QuotaWindow{UsedPercent=100-remaining[i],DurationMinutes=10080}; if(i<3) usage.FiveHour=new QuotaWindow{UsedPercent=100-remaining[i],DurationMinutes=300}; }
                    RingPainter.Draw(g,layout,usage,DateTime.UtcNow,new ModeState{Ultra=i==2,ModelKnown=true,ModelObservedAtUtc=DateTime.UtcNow,Fast=i==2,Working=true,CapturedAtUtc=DateTime.UtcNow});
                    using(Brush avatar=new SolidBrush(Color.FromArgb(229,196,28))) g.FillEllipse(avatar,9,129,36,36);
                    g.Restore(state);
                    g.DrawString(captions[i],caption,ink,25+i*195,245);
                }
                image.Save(path,ImageFormat.Png);
            }
        }
        static void EnableDpi()
        {
            try { if(SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch(EntryPointNotFoundException) { }
            try { SetProcessDpiAwareness(2); } catch { SetProcessDPIAware(); }
        }
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    }

    sealed class OverlayContext : ApplicationContext
    {
        OverlayForm form=new OverlayForm();
        readonly RenderGuard renderGuard=new RenderGuard();
        int reportedRenderFailures;
        string lastDisplayState;
        readonly Queue<object> displayTransitions=new Queue<object>();
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=33};
        readonly EventWaitHandle stop;
        readonly WindowLocator locator=new WindowLocator();
        readonly UsageProvider provider=new UsageProvider();
        readonly ModeDetector modeDetector=new ModeDetector();
        volatile ModeState mode;
        volatile bool readingMode;
        DateTime nextMode=DateTime.MinValue;
        IntPtr lastModeHost=IntPtr.Zero;
        DateTime lastModeHostSeen=DateTime.MinValue;
        volatile UsageSnapshot usage=new UsageSnapshot();
        volatile AnchorResult anchor;
        volatile bool readingUsage, readingAnchor, closing;
        DateTime nextUsage=DateTime.MinValue, nextAnchor=DateTime.MinValue, nextStatus=DateTime.MinValue;
        DateTime anchorAt=DateTime.MinValue;
        public OverlayContext(EventWaitHandle stopEvent)
        {
            stop=stopEvent;
            tray=new NotifyIcon{Icon=SystemIcons.Information,Visible=true,Text="Codex 用量圆环 · 7d / 5h"};
            ContextMenuStrip menu=new ContextMenuStrip();
            menu.Items.Add("刷新额度",null,delegate { nextUsage=DateTime.MinValue; });
            menu.Items.Add("退出圆环",null,delegate { stop.Set(); });
            tray.ContextMenuStrip=menu;
            timer.Tick+=Tick; timer.Start();
        }
        void Tick(object sender,EventArgs e)
        {
            if(stop.WaitOne(0)) { ExitThread(); return; }
            DateTime now=DateTime.UtcNow;
            if(!readingUsage && now>=nextUsage) {
                readingUsage=true; nextUsage=now.AddSeconds(60);
                Task.Factory.StartNew(delegate {
                    try { UsageSnapshot fetched=provider.Fetch(); if(!closing) usage=fetched; }
                    catch { if(!closing) usage=new UsageSnapshot{Status="读取额度失败"}; }
                    finally { readingUsage=false; }
                });
            }
            if(!readingAnchor && now>=nextAnchor && !(anchor!=null && anchor.Found && WindowLocator.IsMoving(anchor.Hwnd))) {
                readingAnchor=true; nextAnchor=now.AddMilliseconds(500);
                Task.Factory.StartNew(delegate {
                    try { AnchorResult found=locator.FindAnchor(); if(!closing && (found.Found || anchor==null || !anchor.Found || (DateTime.UtcNow-anchorAt).TotalSeconds>=2 && !WindowLocator.IsMoving(anchor.Hwnd))) { anchor=found; anchorAt=DateTime.UtcNow; } }
                    catch { anchor=null; }
                    finally { readingAnchor=false; }
                });
            }
            AnchorResult current=anchor;
            bool show=current!=null && current.Found && ((now-anchorAt).TotalSeconds<2 || WindowLocator.IsMoving(current.Hwnd)) && WindowLocator.IsForeground(current.Hwnd);
            Rectangle actualHost=show?WindowLocator.GetHostBounds(current.Hwnd):Rectangle.Empty;
            if(show && (actualHost.IsEmpty || Math.Abs(WindowLocator.GetScale(current.Hwnd)-current.Scale)>.01f)) show=false;
            if(current!=null && current.Found) { lastModeHost=current.Hwnd;lastModeHostSeen=now; }
            if(WindowLocator.IsForeground(lastModeHost) && (now-lastModeHostSeen).TotalMinutes<2 && !readingMode && now>=nextMode) {
                readingMode=true; nextMode=now.AddMilliseconds(250);
                IntPtr hostHandle=lastModeHost;
                Task.Factory.StartNew(delegate { try { var found=modeDetector.Read(hostHandle); if(!closing) mode=found; } finally { readingMode=false; } });
            }
            if(show) {
                Rectangle movingAvatar=current.Avatar;
                movingAvatar.Offset(actualHost.Left-current.HostBounds.Left,actualHost.Bottom-current.HostBounds.Bottom);
                RingLayout layout=RingLayout.FromAvatar(movingAvatar);
                show=renderGuard.TryRender(delegate(bool allowMotion) {
                    ModeState displayMode=mode;
                    
                    if(form.IsDisposed)form=new OverlayForm();
                    form.BindToHost(current.Hwnd);
                    if(!form.Visible)form.Show();
                    form.Render(layout,usage,displayMode,allowMotion);
                },now);
                if(!show && form.Visible)form.Hide();
                if(renderGuard.FailureCount!=reportedRenderFailures) {
                    reportedRenderFailures=renderGuard.FailureCount;
                    try{Program.WriteJson(Path.Combine(Program.DataDirectory,"render-error.json"),renderGuard);}catch{}
                }
            } else if(form.Visible) form.Hide();
            if(now>=nextStatus) {
                nextStatus=now.AddSeconds(2);
                string display=Program.DisplayState(show,mode,renderGuard,now);
                string displayKey=display+":"+(mode==null?"unknown":mode.ModelSource+":"+mode.Ultra);
                if(displayKey!=lastDisplayState) {
                    lastDisplayState=displayKey;
                    displayTransitions.Enqueue(new { at=now.ToString("o"),state=display,fastSource=mode==null?"unknown":mode.FastSource,modelSource=mode==null?"unknown":mode.ModelSource,ultra=mode!=null&&mode.Ultra });
                    while(displayTransitions.Count>64)displayTransitions.Dequeue();
                    try{Program.WriteJson(Path.Combine(Program.DataDirectory,"display-transitions.json"),displayTransitions.ToArray());}catch{}
                }
                try { Program.WriteJson(Path.Combine(Program.DataDirectory,"status.json"),Program.Status(current,usage,show,mode,renderGuard,form.Embedded,new { visible=form.TooltipVisible, showCount=form.TooltipShowCount, target=form.HoverTarget, errorCount=form.TooltipErrorCount, errorType=form.TooltipErrorType })); } catch { }
            }
        }
        protected override void ExitThreadCore()
        {
            closing=true; timer.Stop(); provider.Cancel(); tray.Visible=false; form.Close(); base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) { timer.Dispose(); if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose(); tray.Dispose(); form.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
