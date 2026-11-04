using System;using CodexUsageRings;
public static class ModelSelectionTests {
 static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
 public static int Run(){count=0;var t=new DateTime(2026,11,4,9,0,0,DateTimeKind.Utc);var mem=new ModelSelectionMemory();
 var ultra=new ModeState();mem.Apply(ultra,"h1","chat-A","GPT-6 Astra Ultra",false,t);
 Check(ultra.Ultra&&ultra.ModelKnown,"model button confirms Ultra");
 Check(ultra.ModelFresh(t.AddSeconds(8)),"focus return retains confirmed model beyond fast sample TTL");
 Check(!ultra.ModelFresh(t.AddMinutes(3)),"model freshness is bounded");
 var menu=new ModeState();mem.Apply(menu,"h1","chat-A","",true,t.AddSeconds(5));Check(menu.Ultra&&menu.ModelKnown,"missing model while menu opens retains Ultra");
 var focus=new ModeState();mem.Apply(focus,"h1","chat-A","",false,t.AddSeconds(8));Check(focus.Ultra,"focus change in same chat retains Ultra");
 var failed=new ModeState();mem.Apply(failed,"h1","","",false,t.AddSeconds(9));Check(failed.Ultra,"brief incomplete scan does not imply standard mode");
 var expired=new ModeState();mem.Apply(expired,"h1","","",false,t.AddSeconds(20));Check(!expired.ModelKnown,"unidentified context cannot carry mode indefinitely");
 var medium=new ModeState();mem.Apply(medium,"h1","chat-A","GPT-6 Astra Medium",false,t.AddSeconds(21));Check(medium.ModelKnown&&!medium.Ultra,"explicit Medium clears Ultra immediately");
 mem.Apply(new ModeState(),"h1","chat-A","GPT-6 Astra Ultra",false,t.AddSeconds(22));
 var other=new ModeState();mem.Apply(other,"h1","chat-B","",false,t.AddSeconds(23));Check(!other.ModelKnown&&!other.Ultra,"another chat cannot inherit Ultra");
 mem.Apply(new ModeState(),"h1","chat-A","GPT-6 Astra Ultra",false,t.AddSeconds(24));
 var host=new ModeState();mem.Apply(host,"h2","chat-A","",false,t.AddSeconds(25));Check(!host.ModelKnown,"another window cannot inherit Ultra");
 mem.Apply(new ModeState(),"h1","chat-A","GPT-6 Astra Ultra",false,t.AddSeconds(26));
 var stale=new ModeState();mem.Apply(stale,"h1","chat-A","",true,t.AddMinutes(3));Check(!stale.ModelKnown,"long unconfirmed menu state expires");var partial=new ModelSelectionMemory();
 partial.Apply(new ModeState(),"h1","chat-A","GPT-6 Astra Ultra",false,t);
 partial.Apply(new ModeState(),"h1","","GPT-6 Astra Ultra",false,t.AddSeconds(1));
 var restored=new ModeState();partial.Apply(restored,"h1","chat-A","",false,t.AddSeconds(2));
 Check(restored.Ultra&&restored.ModelKnown,"temporary missing sidebar context must not erase same-chat model memory");foreach(string name in new[]{"GPT-6 Astra Ultra","GPT-6.1 Sol Ultra","GPT-6.1 Terra Ultra","GPT-6 Luna Ultra","Future Model Ultra"}) {
 var detected=ModeDetector.FromLabels(new[]{name},t);Check(detected.ModelKnown&&detected.Ultra,"Ultra effort supported for "+name);
 var cache=new ModelSelectionMemory();cache.Apply(new ModeState(),"host","chat",name,false,t);
 var retained=new ModeState();cache.Apply(retained,"host","chat","",true,t.AddSeconds(1));Check(retained.Ultra,"cached Ultra supported for "+name);
 }
 var namedUltra=ModeDetector.FromLabels(new[]{"GPT-6 Ultra Medium"},t);Check(!namedUltra.Ultra,"Ultra in model name does not imply Ultra effort");
 var menuOption=ModeDetector.FromLabels(new[]{"GPT-6 Astra Medium","Ultra"},t);Check(!menuOption.Ultra,"unselected Ultra option cannot select Ultra effort");
 return count;}
}
