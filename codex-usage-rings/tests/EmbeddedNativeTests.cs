using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;using CodexUsageRings;
class EmbeddedNativeTests {
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
