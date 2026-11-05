using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
namespace CodexUsageRings {
 public sealed class ModeState {
  public bool Ultra,Fast,Working,FastKnown,ModelKnown;
  public string ModelSource="unknown";
  internal string ContextKey="",SelectedModel="";internal bool PickerOpen,ContextAmbiguous;internal DateTime ScanStartedAtUtc;
  public string FastSource="unknown";
  public int FastEventCount;public bool FastListenerActive;
  public DateTime CapturedAtUtc,ModelObservedAtUtc;
  public bool ModelFresh(DateTime now) { return ModelKnown && now>=ModelObservedAtUtc && (now-ModelObservedAtUtc).TotalSeconds<=120; }
  public string[] Evidence=new string[0];
  public string Reason="尚未识别模式";
  public bool Fresh(DateTime now) { return now>=CapturedAtUtc && (now-CapturedAtUtc).TotalSeconds<=4; }
 }
 public sealed class ModelSelectionMemory {
  string lastHost="",lastContext="",lastModel="";DateTime observed;
  public void Apply(ModeState state,string host,string context,string model,bool pickerOpen,DateTime now) {
   // A definite host/chat change invalidates the previous selection even if the new control is absent.
   if(host!=lastHost || (!String.IsNullOrEmpty(context)&&context!=lastContext) || state.ContextAmbiguous) {
    lastModel="";lastHost=host;lastContext=context;
   }
   bool direct=!String.IsNullOrEmpty(model);
   if(direct) {lastModel=model;lastHost=host;if(!String.IsNullOrEmpty(context))lastContext=context;observed=now;}
   double age=(now-observed).TotalSeconds;
   bool sameContext=!String.IsNullOrEmpty(context)&&context==lastContext;
   bool carry=!direct&&!state.ContextAmbiguous&&!String.IsNullOrEmpty(lastModel)&&age>=0&&
    (sameContext || pickerOpen ? age<=120 : age<=10);
   state.ModelKnown=direct||carry;
   state.ModelObservedAtUtc=state.ModelKnown?observed:DateTime.MinValue;
   string selected=direct?model:(carry?lastModel:"");
   state.ModelSource=direct?"uia-model-button":(carry?"confirmed-model-cache":"unknown");
   state.Ultra=state.ModelKnown && ModeDetector.HasUltraEffort(selected);
   if(carry) { state.CapturedAtUtc=now;state.SelectedModel=selected; }
  }
 }
 public sealed class FastModeMemory {
  sealed class Entry { public bool Fast;public string Model;public DateTime Observed; }
  readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
  string lastContext="",lastModel="",pendingContext="",pendingModel="";DateTime lastSeen,pendingAt;bool? pending;

  public void Apply(ModeState state,string context,string model,DateTime now) {
   // Composer labels include effort. Fast belongs to the selected model/chat,
   // so an effort-only change must not invalidate a confirmed checkbox value.
   model=Regex.Replace((model??"").Trim(),@"\s+(?:extra high|xhigh|ultra|high|medium|low|minimal)$","",RegexOptions.IgnoreCase);
   if(String.IsNullOrEmpty(context)||String.IsNullOrEmpty(model)) {
    if(state.FastKnown && !String.IsNullOrEmpty(context)) {
     // The picker can hide the model button before we have ever sampled it.
     // Bind this direct checkbox observation only when this exact chat's
     // model reappears, rather than silently dropping it.
     pending=state.Fast;pendingContext=context;pendingModel="";pendingAt=now;
    } else if(state.FastKnown && !String.IsNullOrEmpty(lastContext) && (now-lastSeen).TotalMinutes<2) {
     pending=state.Fast;pendingContext=lastContext;pendingModel=lastModel;pendingAt=now;
    }
    return;
   }
   if(pending.HasValue) {
    if(!state.FastKnown && context==pendingContext && (pendingModel.Length==0||model==pendingModel) && now>=pendingAt && (now-pendingAt).TotalMinutes<2) {
     state.Fast=pending.Value;state.FastKnown=true;state.FastSource="confirmed-checkbox-cache";
    }
    pending=null;
   }
   lastContext=context;lastModel=model;lastSeen=now;
   if(state.FastKnown) {
    if(entries.Count>64)entries.Clear();
    entries[context]=new Entry{Fast=state.Fast,Model=model,Observed=now};return;
   }
   Entry entry;
   if(entries.TryGetValue(context,out entry)) {
    if(entry.Model!=model || (now-entry.Observed).TotalHours>12) {entries.Remove(context);return;}
    state.Fast=entry.Fast;state.FastKnown=true;state.FastSource="confirmed-checkbox-cache";
   }
  }
 }
 public sealed class FastToggleEvents {
  readonly object gate=new object();string context="";bool fast,pending;DateTime observed;
  public void Record(string owner,bool enabled,DateTime now) {lock(gate){context=owner;fast=enabled;observed=now;pending=true;}}
  public void Apply(ModeState state,DateTime now) {
   lock(gate) {
    if(!pending)return;pending=false;
    if(String.IsNullOrEmpty(context)||context!=state.ContextKey||state.ContextAmbiguous||now<observed||(now-observed).TotalSeconds>5)return;
    if(state.FastKnown&&state.ScanStartedAtUtc>observed)return;
    state.FastKnown=true;state.Fast=fast;state.FastSource="uia-toggle-event";
   }
  }
 }
 public sealed class ModeDetector : IDisposable {
  static readonly object Gate=new object();static Thread worker;
  readonly FastModeMemory fastMemory=new FastModeMemory();
  readonly ModelSelectionMemory modelMemory=new ModelSelectionMemory();
  readonly FastToggleEvents toggleEvents=new FastToggleEvents();
  readonly object watchGate=new object();AutomationElement watched;AutomationPropertyChangedEventHandler watchHandler;string watchedId="",watchContext="";volatile int watchGeneration;volatile bool disposed;int fastEventCount;
  public ModeState Read(IntPtr hwnd) {
   if(hwnd==IntPtr.Zero)return new ModeState();
   ModeState result=null;Thread current;
   lock(Gate) {
    if(worker!=null&&worker.IsAlive)return Resolve(new ModeState{Reason="模式控件仍在读取"},hwnd);
    current=new Thread(delegate(){try{result=Scan(hwnd);}catch{result=new ModeState{Reason="模式控件不可读取"};}});
    current.IsBackground=true;current.SetApartmentState(ApartmentState.MTA);worker=current;current.Start();
   }
   return Resolve(current.Join(1800)&&result!=null?result:new ModeState{Reason="模式读取超时"},hwnd);
  }
  ModeState Resolve(ModeState state,IntPtr hwnd) {
   modelMemory.Apply(state,hwnd.ToString(),state.ContextKey,state.SelectedModel,state.PickerOpen,DateTime.UtcNow);
   toggleEvents.Apply(state,DateTime.UtcNow);
   fastMemory.Apply(state,state.ContextAmbiguous?"":state.ContextKey,state.SelectedModel,DateTime.UtcNow);
   lock(watchGate){watchContext=state.ContextAmbiguous?"":state.ContextKey;state.FastListenerActive=watched!=null;}
   state.FastEventCount=fastEventCount;
   return state;
  }
  void WatchFastControl(AutomationElement control) {
   lock(watchGate) {
    if(disposed||String.IsNullOrEmpty(watchContext))return;
    string id=String.Join(",",control.GetRuntimeId())+":"+watchContext;
    if(id==watchedId)return;
    ClearWatch();int generation=watchGeneration;string owner=watchContext;
    AutomationPropertyChangedEventHandler handler=delegate(object sender,AutomationPropertyChangedEventArgs e) {
     try {
      if(disposed||generation!=watchGeneration)return;
      // ToggleState is the actual switch value. Its label can lag by a frame.
      if(e.Property==TogglePattern.ToggleStateProperty) {
       ToggleState value=(ToggleState)Convert.ToInt32(e.NewValue);
       if(value!=ToggleState.Indeterminate){Interlocked.Increment(ref fastEventCount);toggleEvents.Record(owner,value==ToggleState.On,DateTime.UtcNow);}
      }
     }catch { }
    };
    try {
     Automation.AddAutomationPropertyChangedEventHandler(control,TreeScope.Element,handler,TogglePattern.ToggleStateProperty);
     watched=control;watchHandler=handler;watchedId=id;
    } catch {ClearWatch();}
   }
  }
  void ClearWatch() {
   watchGeneration++;
   if(watched!=null&&watchHandler!=null)try{Automation.RemoveAutomationPropertyChangedEventHandler(watched,watchHandler);}catch{}
   watched=null;watchHandler=null;watchedId="";
  }
  public void Dispose(){lock(watchGate){disposed=true;ClearWatch();}}
  ModeState Scan(IntPtr hwnd) {
   DateTime scanStarted=DateTime.UtcNow;
   Rectangle host=WindowLocator.GetHostBounds(hwnd);float scale=WindowLocator.GetScale(hwnd);
   if(host.IsEmpty)return new ModeState();
   var composer=new System.Windows.Rect(host.Left+72*scale,Math.Max(host.Top,host.Bottom-320*scale),Math.Max(0,host.Width-72*scale),Math.Min(host.Height,320*scale));
   var sidebar=new System.Windows.Rect(host.Left,host.Top,Math.Min(600*scale,host.Width*.42),host.Height);
   var header=new System.Windows.Rect(host.Left,host.Top,host.Width,Math.Min(host.Height,150*scale));
   var cache=new CacheRequest();cache.TreeScope=TreeScope.Element;
   cache.Add(AutomationElement.NameProperty);cache.Add(AutomationElement.BoundingRectangleProperty);cache.Add(AutomationElement.ControlTypeProperty);cache.Add(AutomationElement.IsOffscreenProperty);cache.Add(AutomationElement.ClassNameProperty);
   var walker=TreeWalker.ControlViewWalker;
   var pending=new Stack<Tuple<AutomationElement,int>>();pending.Push(Tuple.Create(AutomationElement.FromHandle(hwnd),0));
   var labels=new List<string>();var contexts=new List<string>();var toolbarContexts=new List<string>();string model="";
   bool? fastSignal=null;bool conflict=false,pickerOpen=false;
   var timer=Stopwatch.StartNew();int nodes=0;
   while(pending.Count>0) {
    var branch=pending.Pop();var child=walker.GetFirstChild(branch.Item1,cache);
    while(child!=null) {
     if(++nodes>1400||timer.ElapsedMilliseconds>1500)return new ModeState{Reason="模式扫描超过时限"};
     var info=child.Cached;var b=info.BoundingRectangle;
     bool bottom=!b.IsEmpty&&b.IntersectsWith(composer),left=!b.IsEmpty&&b.IntersectsWith(sidebar),top=!b.IsEmpty&&b.IntersectsWith(header);
     if(!info.IsOffscreen&&(bottom||left||top)) {
      string name=info.Name??"",css=info.ClassName??"";
      if(top&&info.ControlType==ControlType.ToolBar&&name=="Chat toolbar") {
       var titles=child.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text));
       var names=new List<string>();foreach(AutomationElement title in titles)if(!title.Current.IsOffscreen&&!String.IsNullOrWhiteSpace(title.Current.Name))names.Add(title.Current.Name);
       string key=ChatToolbarContext(hwnd.ToString(),name,names.ToArray(),child.GetRuntimeId());
       if(key.Length>0)toolbarContexts.Add(key);
      }
      if(css.Contains("FastModeToggle")||css.Contains("_ViewToggle_")||css.Contains("_SliderKeyboardControl_"))pickerOpen=true;
      if(left&&info.ControlType==ControlType.Button&&css.Contains("sidebar-item")&&Regex.IsMatch(css,@"(^|\s)bg-primary-ghost-hover(\s|$)")) {
       // Bind to the selected sidebar element, not the most recent chat or a global default.
       using(var hash=SHA256.Create()) { contexts.Add(hwnd.ToString()+":"+Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(name)))); }
      }
      if(bottom&&info.ControlType==ControlType.Button&&(css.Contains("h-token-button-composer")||css.Contains("size-token-button-composer"))&&IsRelevant(name)) {
       labels.Add(name);if(IsModelLabel(name))model=name;
      }
      if(bottom&&css.Contains("FastModeToggle")) {
       ToggleState? toggle=null;object pattern;
       if(child.TryGetCurrentPattern(TogglePattern.Pattern,out pattern)){WatchFastControl(child);toggle=((TogglePattern)pattern).Current.ToggleState;}
       bool value;
       if(TryFastControl(name,info.ControlType,toggle,out value)) {
        if(fastSignal.HasValue&&fastSignal.Value!=value)conflict=true;
        fastSignal=value;labels.Add(name);
       }
      }
     }
     if(!info.IsOffscreen&&branch.Item2<64&&(bottom||left||top||(b.IsEmpty&&branch.Item2<3)))pending.Push(Tuple.Create(child,branch.Item2+1));
     child=walker.GetNextSibling(child,cache);
    }
   }
   if(toolbarContexts.Count>0)contexts=toolbarContexts;
   var state=FromLabels(labels.ToArray(),DateTime.UtcNow);
   state.ScanStartedAtUtc=scanStarted;
   state.ContextKey=contexts.Count==1?contexts[0]:"";state.ContextAmbiguous=contexts.Count>1;state.SelectedModel=model;state.PickerOpen=pickerOpen;
   state.FastKnown=fastSignal.HasValue&&!conflict;state.Fast=state.FastKnown&&fastSignal.Value;state.FastSource=state.FastKnown?"uia-checkbox":"unknown";
   state.Reason="当前输入区与 Fast 开关；聊天定位="+(contexts.Count==1?"唯一":"未知")+"；nodes="+nodes;
   return state;
  }
  public static string ChatToolbarContext(string host,string toolbarName,string[] titles,int[] runtimeId) {
   if(toolbarName!="Chat toolbar"||titles==null||titles.Length!=1||String.IsNullOrWhiteSpace(titles[0])||runtimeId==null||runtimeId.Length==0)return "";
   // Scope by the actual toolbar instance as well as title: two chats with the
   // same title must not share a Fast observation. Never expose raw titles.
   using(var hash=SHA256.Create())return host+":toolbar:"+Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(String.Join(",",runtimeId)+":"+titles[0])));
  }
  public static bool TryFastControl(string name,ControlType type,ToggleState? toggle,out bool enabled) {
   enabled=false;
   if(type!=ControlType.CheckBox&&type!=ControlType.MenuItem&&type!=ControlType.Button)return false;
   string label=(name??"").Trim().ToLowerInvariant();bool? named=null;
   if(label=="enable standard mode"||label=="启用标准模式"||label=="切换到标准模式")named=true;
   if(label=="enable fast mode"||label=="启用快速模式"||label=="切换到快速模式")named=false;
   if(!named.HasValue)return false;
   if(toggle.HasValue&&(toggle==ToggleState.Indeterminate||(toggle==ToggleState.On)!=named.Value))return false;
   enabled=named.Value;return true;
  }
  public static bool IsModelLabel(string name) {
   if(String.IsNullOrWhiteSpace(name)||name.Length>100)return false;
   return Regex.IsMatch(name.Trim(),@"^.+\s+(?:ultra|extra high|xhigh|high|medium|low|minimal)$",RegexOptions.IgnoreCase) ||
    Regex.IsMatch(name,@"^(GPT[- ]|Astra\b|Sol\b|Terra\b|Luna\b)",RegexOptions.IgnoreCase);
  }
  public static bool HasUltraEffort(string name) {
   return IsModelLabel(name) && Regex.IsMatch(name.Trim(),@"\sultra$",RegexOptions.IgnoreCase);
  }
  static bool IsRelevant(string name) {
   if(String.IsNullOrWhiteSpace(name)||name.Length>100)return false;
   return IsModelLabel(name) || Regex.IsMatch(name,@"^(\[off\] )?(GPT[- ]|Astra\b|Sol\b|Terra\b|Luna\b|Ultra\b|Fast\b|Speed\b|Reasoning\b|1[.,]5\s*[x×]|快速|速度|思考|推理|停止|中断|Stop\b|Enable standard mode|Enable fast|Turn on fast)",RegexOptions.IgnoreCase);
  }
  public static ModeState FromLabels(string[] labels,DateTime now) {
   var selected=new List<string>();foreach(string label in labels??new string[0])if(IsRelevant(label))selected.Add(label);
   labels=selected.ToArray();bool fast=false,known=false,working=false,modelKnown=false,ultra=false;
   foreach(string raw in labels) {
    string label=raw.ToLowerInvariant();
    if(IsModelLabel(label)) { modelKnown=true;ultra=HasUltraEffort(label); }
    if(Regex.IsMatch(label,@"^(stop|stop generating|stop response|stop streaming|stop turn|停止|停止生成|停止响应|中断)$"))working=true;
    bool enabled;
    if(TryFastControl(label,ControlType.Button,null,out enabled)) {fast=enabled;known=true;continue;}
    if(label.StartsWith("[off]")||Regex.IsMatch(label,@"enable|turn on|off|standard|启用|开启|关闭|标准"))continue;
    if(Regex.IsMatch(label,@"^fast( mode)?$|^快速(模式)?$")) {fast=true;known=true;}
   }
   return new ModeState{ModelKnown=modelKnown,ModelObservedAtUtc=now,Ultra=ultra,Fast=fast,FastKnown=known,Working=working,CapturedAtUtc=now,Evidence=labels,Reason="当前输入区控件"};
  }
 }
}
