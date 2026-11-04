using System;
using System.Drawing;
using CodexUsageRings;
using System.Windows.Automation;
public static class FastModeTests {
 static int count;
 static void Check(bool condition,string message) { if(!condition)throw new Exception(message);count++; }
 static Bitmap Frame(ModeState mode,DateTime now,double remaining) {
  var b=new Bitmap(36,76);using(var g=Graphics.FromImage(b)) {
   g.Clear(Color.Transparent);
   RingPainter.Draw(g,RingLayout.FromAvatar(new Rectangle(0,100,24,24)),new UsageSnapshot{CapturedAtUtc=now,Week=new QuotaWindow{UsedPercent=100-remaining,DurationMinutes=10080}},now,mode);
  }return b;
 }
 static bool Different(Bitmap a,Bitmap b) { for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return true;return false; }
 public static int Run() {
  count=0;var now=new DateTime(2026,11,4,8,0,0,DateTimeKind.Utc);
  foreach(string effort in new[]{"Low","Medium","High","Extra High","Ultra"})foreach(bool working in new[]{false,true}) {
   var mode=ModeDetector.FromLabels(working?new[]{"GPT-6 Astra "+effort,"Stop","Enable standard mode"}:new[]{"GPT-6 Astra "+effort,"Enable standard mode"},now);
   using(var a=Frame(mode,now,82))using(var b=Frame(mode,now.AddMilliseconds(120),82))Check(Different(a,b),"Fast must animate at "+effort+" working="+working);
   mode.Fast=false;
   using(var a=Frame(mode,now,82))using(var b=Frame(mode,now.AddMilliseconds(120),82))Check(Different(a,b)==mode.Ultra,"Ultra gradient moves independently; ordinary non-Fast mode stays static");
  }
  var fast=new ModeState{Fast=true,Working=true,CapturedAtUtc=now};
  using(var a=Frame(fast,now.AddMilliseconds(300),12))using(var b=Frame(fast,now.AddMilliseconds(420),12))Check(Different(a,b),"flow stays visible along low-quota arc");
  var idleUltra=ModeDetector.FromLabels(new[]{"GPT-6 Astra Ultra"},now);
  var idleMedium=ModeDetector.FromLabels(new[]{"GPT-6 Astra Medium"},now);
  using(var a=Frame(idleUltra,now,82))using(var b=Frame(idleMedium,now,82))Check(Different(a,b),"selecting Ultra must change color even while idle");
  using(var a=Frame(idleUltra,now,82))using(var b=Frame(new ModeState{Ultra=true,ModelKnown=true,ModelObservedAtUtc=now,Working=true,CapturedAtUtc=now},now,82))Check(!Different(a,b),"Ultra color must not depend on generating state");
  bool enabled;
  Check(ModeDetector.TryFastControl("Enable standard mode",ControlType.CheckBox,ToggleState.On,out enabled) && enabled,"actual checked Fast checkbox is recognized");
  Check(ModeDetector.TryFastControl("Enable fast mode",ControlType.CheckBox,ToggleState.Off,out enabled) && !enabled,"actual unchecked Fast checkbox is recognized");
  Check(!ModeDetector.TryFastControl("1.5x speed",ControlType.Text,null,out enabled),"tooltip is not enabled state");
  Check(!ModeDetector.TryFastControl("Enable standard mode",ControlType.CheckBox,ToggleState.Off,out enabled),"inconsistent transition is not accepted");
  var memory=new FastModeMemory();
  memory.Apply(new ModeState{FastKnown=true,Fast=true},"chat-A","Astra High",now);
  var closed=new ModeState();memory.Apply(closed,"chat-A","Astra High",now.AddSeconds(5));
  Check(closed.FastKnown && closed.Fast,"closing menu retains confirmed Fast for same chat and model");
  var other=new ModeState();memory.Apply(other,"chat-B","Astra High",now.AddSeconds(6));Check(!other.FastKnown && !other.Fast,"another chat cannot inherit Fast");
  var changed=new ModeState();memory.Apply(changed,"chat-A","Astra Medium",now.AddSeconds(7));Check(!changed.FastKnown,"unobserved model change invalidates stale state");
  memory.Apply(new ModeState{FastKnown=true,Fast=true},"chat-A","Astra Medium",now.AddSeconds(8));
  memory.Apply(new ModeState{FastKnown=true,Fast=false},"chat-A","Astra Medium",now.AddSeconds(9));
  var disabled=new ModeState();memory.Apply(disabled,"chat-A","Astra Medium",now.AddSeconds(10));Check(disabled.FastKnown && !disabled.Fast,"turning Fast off clears animation after menu closes");
  var unknown=new ModeState();memory.Apply(unknown,"","Astra Medium",now.AddSeconds(10));Check(!unknown.FastKnown,"unidentified chat does not inherit cached state");
  var modalMemory=new FastModeMemory();
  modalMemory.Apply(new ModeState(),"chat-A","Astra High",now);
  modalMemory.Apply(new ModeState{Fast=true,FastKnown=true},"","",now.AddSeconds(1));
  var reopened=new ModeState();modalMemory.Apply(reopened,"chat-A","Astra High",now.AddSeconds(2));
  Check(reopened.FastKnown&&reopened.Fast,"modal menu hides sidebar: bind observed checkbox when same chat reappears");
  modalMemory.Apply(new ModeState{Fast=false,FastKnown=true},"","",now.AddSeconds(3));
  var switchChat=new ModeState();modalMemory.Apply(switchChat,"chat-B","Astra High",now.AddSeconds(4));
  Check(!switchChat.FastKnown,"pending modal observation must not attach to different chat");
  // Exact production failure: 82% remaining, 100% DPI, 08:00:00.005 UTC.
  using(var frame=Frame(new ModeState{Fast=true,CapturedAtUtc=now},now.AddMilliseconds(5),82))Check(frame.Width==36,"reported GDI+ arc failure does not recur");
  return count;
 }
}
