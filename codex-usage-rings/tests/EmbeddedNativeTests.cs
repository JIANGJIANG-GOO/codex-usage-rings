using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;using CodexUsageRings;
class EmbeddedNativeTests {
 static void Set(object target,string name,object value){target.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(target,value);}
 static void CheckBackgroundVisibility(Form host){
 using(var stop=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.ManualReset))
 using(var context=new OverlayContext(stop))using(var other=new Form()){
 var bounds=WindowLocator.GetHostBounds(host.Handle);
 Set(context,"anchor",new AnchorResult{Found=true,Hwnd=host.Handle,HostBounds=bounds,Avatar=new Rectangle(bounds.Left+14,bounds.Bottom-40,24,24),Scale=WindowLocator.GetScale(host.Handle)});
 Set(context,"anchorAt",DateTime.UtcNow);Set(context,"nextAnchor",DateTime.MaxValue);Set(context,"nextUsage",DateTime.MaxValue);Set(context,"nextMode",DateTime.MaxValue);Set(context,"nextStatus",DateTime.MaxValue);
 var tick=typeof(OverlayContext).GetMethod("Tick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
 var overlay=(OverlayForm)typeof(OverlayContext).GetField("form",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(context);
 other.StartPosition=FormStartPosition.Manual;other.Bounds=new Rectangle(-3000,-3000,300,300);other.ShowInTaskbar=false;other.Show();other.Activate();Application.DoEvents();
 if(WindowLocator.IsForeground(host.Handle))throw new Exception("Background test host still foreground");
 tick.Invoke(context,new object[]{null,EventArgs.Empty});
 if(!overlay.Visible||!overlay.Embedded)throw new Exception("Background host lost embedded quota rings");
 host.WindowState=FormWindowState.Minimized;Application.DoEvents();tick.Invoke(context,new object[]{null,EventArgs.Empty});
 if(overlay.Visible)throw new Exception("Minimized host must hide rings");
 host.WindowState=FormWindowState.Normal;other.Activate();Application.DoEvents();Set(context,"anchorAt",DateTime.UtcNow);tick.Invoke(context,new object[]{null,EventArgs.Empty});
 if(!overlay.Visible)throw new Exception("Restored background host did not recover rings");
 host.Hide();tick.Invoke(context,new object[]{null,EventArgs.Empty});if(overlay.Visible)throw new Exception("Hidden host must hide rings");host.Show();
 }
 }
 [StructLayout(LayoutKind.Sequential)]struct R{public int L,T,Ri,B;}
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out R r);
 [DllImport("kernel32.dll")]static extern uint SetErrorMode(uint mode);
 [STAThread]static int Main(){SetErrorMode(3);Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);try{
 using(var host=new Form())using(var child=new OverlayForm()){
 host.StartPosition=FormStartPosition.Manual;host.Bounds=new Rectangle(-2000,-2000,600,500);host.ShowInTaskbar=false;host.Show();Application.DoEvents();
 child.BindToHost(host.Handle);child.Show();
 var b=WindowLocator.GetHostBounds(host.Handle);var avatar=new Rectangle(b.Left+14,b.Bottom-40,24,24);
 var now=DateTime.UtcNow;child.Render(RingLayout.FromAvatar(avatar),new UsageSnapshot{CapturedAtUtc=now,Week=new QuotaWindow{UsedPercent=20}},new ModeState{Ultra=true,Fast=true,CapturedAtUtc=now});
 if(!child.Embedded)throw new Exception("Not a native child");R first;GetWindowRect(child.Handle,out first);
 for(int i=1;i<=100;i++){host.Location=new Point(-2000+i,-2000+i);Application.DoEvents();R moved;GetWindowRect(child.Handle,out moved);if(moved.L-first.L!=i||moved.T-first.T!=i)throw new Exception("Child did not follow parent without render: "+i);}
 CheckBackgroundVisibility(host);
 host.Close();Application.DoEvents();
 using(var second=new Form()) {
 second.StartPosition=FormStartPosition.Manual;second.Bounds=new Rectangle(-2000,-2000,600,500);second.ShowInTaskbar=false;second.Show();Application.DoEvents();
 if(child.IsDisposed)throw new Exception("Host close disposed companion child");
 child.BindToHost(second.Handle);child.Show();
 if(!child.Embedded)throw new Exception("Cannot reattach after host close");
 var area=WindowLocator.GetHostBounds(second.Handle);
 child.Render(RingLayout.FromAvatar(new Rectangle(area.Left+14,area.Bottom-40,24,24)),new UsageSnapshot{CapturedAtUtc=DateTime.UtcNow,Week=new QuotaWindow{UsedPercent=20}},new ModeState{Ultra=true,CapturedAtUtc=DateTime.UtcNow});
 }
 Console.WriteLine("PASS native parent attachment, layered rendering, 100 parent moves without child polling, parent close and reattach/render");
 }return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
