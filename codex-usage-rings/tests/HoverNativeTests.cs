using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodexUsageRings;

// Separate executable: compile with app.manifest. Both windows stay off-screen.
internal static class HoverNativeTests
{
    static int assertions;
    [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    static void Check(bool result,string label) { if(!result)throw new Exception(label);assertions++; }
    static Point Center(OverlayForm form,RectangleF circle) { return form.PointToScreen(new Point((int)(circle.Left+circle.Width/2),(int)(circle.Top+circle.Height/2))); }
    [STAThread] static int Main()
    {
        SetErrorMode(0x0001|0x0002|0x8000);
        try {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            using(var host=new Form())using(var child=new OverlayForm()) {
                host.StartPosition=FormStartPosition.Manual;host.Bounds=new Rectangle(-4000,-4000,600,500);host.ShowInTaskbar=false;
                if(!SetWindowPos(host.Handle,IntPtr.Zero,-4000,-4000,600,500,0x0010|0x0040|0x0004))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ((Timer)typeof(OverlayForm).GetField("hoverTimer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(child)).Stop();
                child.BindToHost(host.Handle);child.Show();
                Rectangle area=WindowLocator.GetHostBounds(host.Handle);
                RingLayout layout=RingLayout.FromAvatar(new Rectangle(area.Left+14,area.Bottom-40,24,24));
                DateTime now=DateTime.UtcNow;
                UsageSnapshot usage=new UsageSnapshot { CapturedAtUtc=now,Week=new QuotaWindow { UsedPercent=20 },Source="native-tooltip-test",Status="" };
                child.Render(layout,usage,null);
                Check(child.Embedded && child.TopLevelControl==null,"native embedding has no managed tooltip owner");
                Point week=Center(child,layout.Week);
                IntPtr foreground=GetForegroundWindow();
                child.UpdateHover(week,true,now);
                child.UpdateHover(week,true,now.AddMilliseconds(349));
                Check(!child.TooltipVisible,"hover delay must be honored");
                child.UpdateHover(week,true,now.AddMilliseconds(350));
                Check(child.TooltipVisible && child.HoverTarget=="7d","7d tooltip appears over transparent circle center");
                var popup=(Form)typeof(OverlayForm).GetField("tip",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(child);
                Check(popup.TopLevel && IsWindowVisible(popup.Handle),"native popup is independently visible");
                Check(popup.AccessibleDescription.Contains("80%"),"remaining quota is shown");
                Check(GetForegroundWindow()==foreground,"tooltip must not change foreground focus");
                long style=GetWindowLongPtr(popup.Handle,-20).ToInt64();
                Check((style&0x08000020)==0x08000020,"popup is nonactivating and input transparent");
                Check(SendMessage(popup.Handle,0x84,IntPtr.Zero,IntPtr.Zero).ToInt64()==-1,"popup hit test passes through");
                Check(child.TooltipShowCount==1,"one presentation per hover");
                // Exercise the production window-ancestry path and drain posted
                // WinForms messages; the former test checked focus only synchronously
                // and missed activation scheduled by Form.Show.
                for(int i=1;i<=60;i++) {
                    child.Render(layout,usage,null);
                    child.ApplyHoverSample(week,child.Handle,now.AddMilliseconds(350+i*75));
                    Application.DoEvents();
                    Check(GetForegroundWindow()==foreground,"hover must not activate after queued messages "+i);
                    Check(child.TooltipVisible && child.TooltipShowCount==1,"repeated render/hover must not flash "+i);
                }
                child.UpdateHover(week,false,now.AddMilliseconds(400));
                Check(!child.TooltipVisible && child.HoverTarget==null,"occluded or unfocused host hides tooltip");
                child.UpdateHover(Center(child,layout.FiveHour),true,now.AddMilliseconds(500));
                Check(child.HoverTarget==null,"missing 5h does not expose a hover target");
                usage.FiveHour=new QuotaWindow {UsedPercent=25};child.Render(layout,usage,null);
                Point five=Center(child,layout.FiveHour);
                child.UpdateHover(five,true,now.AddMilliseconds(1000));
                child.UpdateHover(five,true,now.AddMilliseconds(1400));
                Check(child.TooltipVisible && child.HoverTarget=="5h" && popup.AccessibleDescription.Contains("75%"),"valid 5h hover shows its remaining quota");
                child.UpdateHover(child.PointToScreen(new Point(-20,-20)),true,now.AddMilliseconds(1500));
                Check(!child.TooltipVisible,"leaving ring hides tooltip");
                child.UpdateHover(week,true,now.AddMilliseconds(1600));child.UpdateHover(week,true,now.AddMilliseconds(2000));
                child.Hide();Check(!child.TooltipVisible,"hiding embedded ring hides popup");
                child.Show();child.UpdateHover(week,true,now.AddMilliseconds(2100));child.UpdateHover(week,true,now.AddMilliseconds(2500));
                child.Dispose();Check(popup.IsDisposed,"disposing embedded ring disposes popup");
            }
            Console.WriteLine("PASS native hover tooltip "+assertions+" assertions: quota, hover delay, transparent center, unchanged focus, input passthrough, leave/hide/dispose");
            return 0;
        } catch(Exception error) {
            Console.Error.WriteLine("FAIL native hover tooltip: "+error.GetType().Name+": "+error.Message);
            return 1;
        }
    }
}
