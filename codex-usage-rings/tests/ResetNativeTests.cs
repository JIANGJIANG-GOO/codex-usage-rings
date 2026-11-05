using System;using System.Drawing;using System.Reflection;using System.Threading;using System.Runtime.InteropServices;using System.Windows.Forms;using CodexUsageRings;
internal static class ResetNativeTests {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 sealed class Fake:IResetCardService{public int Calls,Reads,ReadDelay;public bool Disposed;public ResetCardList Read(){Reads++;if(ReadDelay>0)Thread.Sleep(ReadDelay);var list=ResetCardService.ParseList(ResetCardTests.ListJson("fake-account","available","codexRateLimits",null));for(int i=1;i<=2;i++)list.Cards.Add(new ResetCard{Id="fake-card-"+i,Title="Full reset",Status="available",ResetType="codexRateLimits",ExpiresAtUtc=DateTime.UtcNow.AddDays(i+1)});return list;}public ResetResult Consume(ResetCardList list,string id){Calls++;Thread.Sleep(150);return new ResetResult{Outcome="reset",Message="模拟重置成功（未访问服务器）"};}public void Dispose(){Disposed=true;}}
 static void Pump(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(3);while(!done()&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}Check(done(),"asynchronous panel operation completes");}
 static void StopTimer(ResetCardsForm panel){((System.Windows.Forms.Timer)typeof(ResetCardsForm).GetField("timer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel)).Stop();}
 static void Click(IntPtr h,int x,int y){IntPtr p=new IntPtr((y<<16)|(x&65535));SendMessage(h,0x201,new IntPtr(1),p);SendMessage(h,0x202,IntPtr.Zero,p);}
 [STAThread]static int Main(string[] args){SetErrorMode(3);Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);try{
  if(args.Length==1&&args[0]=="--read-only"){using(var service=new ResetCardService()){var list=service.Read();Console.WriteLine("Read only: error="+list.Error+", available="+list.AvailableCount+", rows="+list.Cards.Count+", details="+list.DetailsKnown+", accountBound="+!String.IsNullOrEmpty(list.AccountId));return list.Error==null?0:1;}}
  var fake=new Fake();using(var host=new Form())using(var overlay=new OverlayForm(fake)){
   var work=new Rectangle(0,0,1920,1040);var popupSize=new Size(320,202);
   var maximum=ResetCardsForm.PositionPanel(new Point(50,942),work,work,popupSize);
   var restoredHost=new Rectangle(100,100,900,700);
   var restored=ResetCardsForm.PositionPanel(new Point(150,702),restoredHost,work,popupSize);
   Check(maximum.Y+popupSize.Height==work.Bottom&&restored.Y+popupSize.Height==restoredHost.Bottom,"maximized and restored panel retain bottom alignment");
   Check(restored.X-restoredHost.X==maximum.X,"horizontal ring-relative offset stays fixed");
   var taller=ResetCardsForm.PositionPanel(new Point(150,702),restoredHost,work,new Size(320,300));
   Check(taller.Y+300==restored.Y+202,"card count changes preserve bottom anchor");
   host.StartPosition=FormStartPosition.Manual;host.Bounds=new Rectangle(-4000,-4000,700,600);host.ShowInTaskbar=false;
   SetWindowPos(host.Handle,IntPtr.Zero,-4000,-4000,700,600,0x0010|0x0040|0x0004);
   overlay.BindToHost(host.Handle);overlay.Show();var area=WindowLocator.GetHostBounds(host.Handle);
   var layout=RingLayout.FromAvatar(new Rectangle(area.Left+14,area.Bottom-40,24,24));overlay.Render(layout,new UsageSnapshot{CapturedAtUtc=DateTime.UtcNow,Week=new QuotaWindow{UsedPercent=35}},null);
   overlay.PrepareCards();
   var preloaded=(ResetCardsForm)typeof(OverlayForm).GetField("cards",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(overlay);
   Pump(()=>preloaded.CurrentList!=null);Check(!preloaded.Presented,"preload never opens panel");int initialReads=fake.Reads;
   IntPtr foreground=GetForegroundWindow();
   Click(overlay.Handle,18,18);Check(overlay.CardsVisible,"native ring click opens cards");
   var panel=(ResetCardsForm)typeof(OverlayForm).GetField("cards",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(overlay);StopTimer(panel);
   Check(fake.Reads==initialReads,"opening fresh cache starts no RPC");
   Pump(()=>panel.CurrentList!=null);Check(panel.CurrentList.Cards.Count==3,"card information loaded");Check(GetForegroundWindow()==foreground,"panel opens without stealing focus");
   Click(panel.Handle,panel.Width-46,60);Check(panel.Selected=="card-A"&&fake.Calls==0,"first native click only shows Confirm");
   Click(panel.Handle,panel.Width-46,60);Check(fake.Calls==0&&!panel.Busy,"immediate second click cannot consume");
   using(var bmp=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reset-panel-test.png"));}
   Thread.Sleep(550);Click(panel.Handle,panel.Width-46,60);Check(panel.Busy,"later second click starts exactly one request");
   Click(panel.Handle,panel.Width-46,60);Pump(()=>!panel.Busy);Check(fake.Calls==1,"busy native clicks do not submit another request");
   overlay.Render(layout,new UsageSnapshot{Source="app-server",CapturedAtUtc=DateTime.UtcNow.AddMilliseconds(10),Week=new QuotaWindow{UsedPercent=0}},null);
   var refill=(ResetRefill)typeof(OverlayForm).GetField("refill",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(overlay);
   var clock=(System.Diagnostics.Stopwatch)typeof(OverlayForm).GetField("animationClock",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(overlay);
   var fillFrame=refill.Sample(false,clock.ElapsedMilliseconds);Check(fillFrame!=null&&fillFrame.Remaining>=65&&fillFrame.Remaining<100,"successful mock reset plus fresh quota starts fill from prior value");
   Click(panel.Handle,panel.Width-46,60);Check(panel.Selected!=null,"new operation requires new confirmation");panel.HidePanel();Check(panel.Selected==null&&!panel.Presented,"closing cancels confirmation");
   overlay.ToggleCards();StopTimer(panel);Pump(()=>panel.CurrentList!=null);Check(panel.Selected==null,"reopening cannot reuse confirmation");
   Check(GetForegroundWindow()==foreground,"request completion does not steal focus");
   var loadingField=typeof(ResetCardsForm).GetField("loading",BindingFlags.NonPublic|BindingFlags.Instance);
   panel.LoadCards();Pump(()=>!(bool)loadingField.GetValue(panel));int stableHeight=panel.Height;
   fake.ReadDelay=600;int beforeRefresh=fake.Reads;Click(panel.Handle,panel.Width-40,24);Pump(()=>fake.Reads>beforeRefresh);Application.DoEvents();Check(panel.Presented,"top-right refresh does not close panel");
   Check(panel.Height==stableHeight,"refresh does not change panel height");
   Check((string)typeof(ResetCardsForm).GetField("message",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel)=="","refresh shows no loading text");
   var refreshRect=(Rectangle)typeof(ResetCardsForm).GetField("refresh",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel);
   var cardButtons=(Rectangle[])typeof(ResetCardsForm).GetField("buttons",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel);
   Check(refreshRect.Left==cardButtons[0].Left&&refreshRect.Size==cardButtons[0].Size,"refresh block aligns with reset blocks");
   using(var bmp=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reset-loading-test.png"));}
   Click(panel.Handle,panel.Width-40,24);Click(panel.Handle,panel.Width-46,60);Check(fake.Calls==1,"loading blocks reset submission");
   Pump(()=>!(bool)loadingField.GetValue(panel));Check(fake.Reads==beforeRefresh+1,"repeated refresh while loading submits only once");fake.ReadDelay=0;

   Click(panel.Handle,20,38);Check(!panel.Presented,"clicking non-action space dismisses panel");overlay.ToggleCards();StopTimer(panel);

   ((System.Windows.Forms.Timer)typeof(ResetCardsForm).GetField("timer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel)).Start();
   Pump(()=>!panel.Presented);Check(panel.Selected==null,"foreground-loss timer cancels confirmation and dismisses panel");
   overlay.ToggleCards();StopTimer(panel);
   overlay.Hide();Check(!panel.Presented,"hidden overlay dismisses panel");
  }Check(fake.Disposed,"service disposed with overlay");Console.WriteLine("PASS reset native panel "+count+" assertions; fake service only, no real consumption");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
 [DllImport("kernel32.dll")]static extern uint SetErrorMode(uint m);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
}
