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
  var changed=new ModeState();memory.Apply(changed,"chat-A","Astra Medium",now.AddSeconds(7));Check(changed.FastKnown&&changed.Fast,"effort-only change retains confirmed Fast");
  foreach(string effort in new[]{"Low","Medium","High","Extra High","XHigh","Ultra","Minimal"}) {
   var effortState=new ModeState();memory.Apply(effortState,"chat-A","Astra "+effort,now.AddSeconds(7));
   Check(effortState.FastKnown&&effortState.Fast,"Fast survives effort change to "+effort);
  }
  var differentModel=new ModeState();memory.Apply(differentModel,"chat-A","Sol Medium",now.AddSeconds(7));Check(!differentModel.FastKnown,"actual model change invalidates stale Fast");
  memory.Apply(new ModeState{FastKnown=true,Fast=true},"chat-A","Astra Medium",now.AddSeconds(8));
  memory.Apply(new ModeState{FastKnown=true,Fast=false},"chat-A","Astra Medium",now.AddSeconds(9));
  var disabled=new ModeState();memory.Apply(disabled,"chat-A","Astra Medium",now.AddSeconds(10));Check(disabled.FastKnown && !disabled.Fast,"turning Fast off clears animation after menu closes");
  var disabledEffort=new ModeState();memory.Apply(disabledEffort,"chat-A","Astra Ultra",now.AddSeconds(10));Check(disabledEffort.FastKnown&&!disabledEffort.Fast,"effort change must also retain confirmed Fast off");
  var unknown=new ModeState();memory.Apply(unknown,"","Astra Medium",now.AddSeconds(10));Check(!unknown.FastKnown,"unidentified chat does not inherit cached state");
  var modalMemory=new FastModeMemory();
  modalMemory.Apply(new ModeState(),"chat-A","Astra High",now);
  modalMemory.Apply(new ModeState{Fast=true,FastKnown=true},"","",now.AddSeconds(1));
  var reopened=new ModeState();modalMemory.Apply(reopened,"chat-A","Astra High",now.AddSeconds(2));
  Check(reopened.FastKnown&&reopened.Fast,"modal menu hides sidebar: bind observed checkbox when same chat reappears");
  modalMemory.Apply(new ModeState{Fast=false,FastKnown=true},"","",now.AddSeconds(3));
  var switchChat=new ModeState();modalMemory.Apply(switchChat,"chat-B","Astra High",now.AddSeconds(4));
  Check(!switchChat.FastKnown,"pending modal observation must not attach to different chat");
  var effortMemory=new FastModeMemory();effortMemory.Apply(new ModeState(),"chat-A","GPT-6 Astra High",now);
  effortMemory.Apply(new ModeState{FastKnown=true,Fast=true},"","",now.AddSeconds(1));
  var afterEffort=new ModeState();effortMemory.Apply(afterEffort,"chat-A","GPT-6 Astra Ultra",now.AddSeconds(2));
  Check(afterEffort.FastKnown&&afterEffort.Fast,"pending menu checkbox survives effort selection before menu closes");
  var toolbarKey=ModeDetector.ChatToolbarContext("host","Chat toolbar",new[]{"Chat A"},new[]{42,7});
  Check(toolbarKey.Length>0&&!toolbarKey.Contains("Chat A"),"toolbar identifies a chat outside visible sidebar without exposing its title");
  Check(toolbarKey==ModeDetector.ChatToolbarContext("host","Chat toolbar",new[]{"Chat A"},new[]{42,7}),"toolbar key remains stable across menu visibility");
  Check(toolbarKey!=ModeDetector.ChatToolbarContext("host","Chat toolbar",new[]{"Chat A"},new[]{42,8}),"same-title distinct toolbar does not share state");
  Check(toolbarKey!=ModeDetector.ChatToolbarContext("host","Chat toolbar",new[]{"Chat B"},new[]{42,7}),"reused toolbar with changed chat title changes identity");
  Check(ModeDetector.ChatToolbarContext("host","Browser toolbar",new[]{"Chat A"},new[]{42,7})=="","browser toolbar is rejected");
  Check(ModeDetector.ChatToolbarContext("host","Chat toolbar",new[]{"Chat A","Chat B"},new[]{42,7})=="","ambiguous toolbar titles are rejected");
  var toolbarMemory=new FastModeMemory();toolbarMemory.Apply(new ModeState{FastKnown=true,Fast=true},toolbarKey,"Astra Medium",now);
  var toolbarClosed=new ModeState();toolbarMemory.Apply(toolbarClosed,toolbarKey,"Astra Medium",now.AddSeconds(15));
  Check(toolbarClosed.FastKnown&&toolbarClosed.Fast,"menu closes with no selected sidebar row: toolbar retains Fast");
  toolbarMemory.Apply(new ModeState{FastKnown=true,Fast=false},toolbarKey,"Astra Medium",now.AddSeconds(16));
  var toolbarOff=new ModeState();toolbarMemory.Apply(toolbarOff,toolbarKey,"Astra Medium",now.AddSeconds(17));
  Check(toolbarOff.FastKnown&&!toolbarOff.Fast,"toolbar cache respects explicitly disabled Fast");
  var coldMenuMemory=new FastModeMemory();
  coldMenuMemory.Apply(new ModeState{FastKnown=true,Fast=true},toolbarKey,"",now);
  var coldMenuClosed=new ModeState();coldMenuMemory.Apply(coldMenuClosed,toolbarKey,"GPT-6 Astra Medium",now.AddSeconds(6));
  Check(coldMenuClosed.FastKnown&&coldMenuClosed.Fast,"live trace: known chat and Fast, absent model, then menu closes must bind pending observation");
  coldMenuMemory.Apply(new ModeState{FastKnown=true,Fast=false},toolbarKey,"",now.AddSeconds(7));
  var coldOff=new ModeState();coldMenuMemory.Apply(coldOff,toolbarKey,"GPT-6 Astra Medium",now.AddSeconds(8));
  Check(coldOff.FastKnown&&!coldOff.Fast,"model hidden while turning Fast off must override old cache");
  var differentPending=new FastModeMemory();differentPending.Apply(new ModeState{FastKnown=true,Fast=true},toolbarKey,"",now);
  var differentRestored=new ModeState();differentPending.Apply(differentRestored,"other-chat","GPT-6 Astra Medium",now.AddSeconds(5));
  Check(!differentRestored.FastKnown,"cold pending observation must not cross chats");
  var expiredPending=new FastModeMemory();expiredPending.Apply(new ModeState{FastKnown=true,Fast=true},toolbarKey,"",now);
  var expiredRestored=new ModeState();expiredPending.Apply(expiredRestored,toolbarKey,"GPT-6 Astra Medium",now.AddMinutes(3));
  Check(!expiredRestored.FastKnown,"cold pending observation expires after two minutes");
  var events=new FastToggleEvents();
  events.Record("chat-A",false,now.AddMilliseconds(150));
  var staleOn=new ModeState{ContextKey="chat-A",FastKnown=true,Fast=true,ScanStartedAtUtc=now};
  events.Apply(staleOn,now.AddMilliseconds(300));
  Check(staleOn.FastKnown&&!staleOn.Fast&&staleOn.FastSource=="uia-toggle-event","off event during scan overrides earlier on sample");
  var eventMemory=new FastModeMemory();eventMemory.Apply(staleOn,"chat-A","Astra High",now.AddMilliseconds(300));
  var quicklyClosed=new ModeState();eventMemory.Apply(quicklyClosed,"chat-A","Astra High",now.AddSeconds(1));
  Check(quicklyClosed.FastKnown&&!quicklyClosed.Fast,"quick close after off event does not resurrect on cache");
  events.Record("chat-A",true,now);var freshOff=new ModeState{ContextKey="chat-A",FastKnown=true,Fast=false,ScanStartedAtUtc=now.AddSeconds(1)};
  events.Apply(freshOff,now.AddSeconds(2));Check(!freshOff.Fast,"newer direct scan wins over an old event");
  events.Record("chat-A",true,now);var noControl=new ModeState{ContextKey="chat-A",ScanStartedAtUtc=now.AddMilliseconds(100)};
  events.Apply(noControl,now.AddMilliseconds(200));Check(noControl.FastKnown&&noControl.Fast,"event survives removal of menu control before next scan");
  events.Record("chat-A",true,now);var wrongChat=new ModeState{ContextKey="chat-B"};events.Apply(wrongChat,now.AddSeconds(1));Check(!wrongChat.FastKnown,"event cannot cross chats");
  events.Record("chat-A",true,now);var lateEvent=new ModeState{ContextKey="chat-A"};events.Apply(lateEvent,now.AddSeconds(6));Check(!lateEvent.FastKnown,"stale event is discarded");
  events.Record("chat-A",true,now);events.Record("chat-A",false,now.AddMilliseconds(50));var rapid=new ModeState{ContextKey="chat-A"};events.Apply(rapid,now.AddMilliseconds(80));Check(rapid.FastKnown&&!rapid.Fast,"latest rapid on-off event wins");
  var consumed=new ModeState{ContextKey="chat-A"};events.Apply(consumed,now.AddMilliseconds(90));Check(!consumed.FastKnown,"an event is consumed once and cannot overwrite later cache");
  // Exact production failure: 82% remaining, 100% DPI, 08:00:00.005 UTC.
  using(var frame=Frame(new ModeState{Fast=true,CapturedAtUtc=now},now.AddMilliseconds(5),82))Check(frame.Width==36,"reported GDI+ arc failure does not recur");
  return count;
 }
}
