using System;using System.Runtime.InteropServices;using CodexUsageRings;
public static class RenderSafetyTests {
 static int count;static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 public static int Run(){count=0;var now=DateTime.UtcNow;var guard=new RenderGuard();int calls=0;bool lastMotion=true;
 bool shown=guard.TryRender(delegate(bool motion){calls++;lastMotion=motion;if(motion)throw new OutOfMemoryException("synthetic GDI+ failure");},now);
 Check(shown&&calls==2&&!lastMotion,"drawing OOM falls back to a static frame without escaping");
 Check(guard.FailureCount==1&&guard.LastErrorType=="OutOfMemoryException","failure is recorded for diagnostics");
 guard.TryRender(delegate(bool motion){lastMotion=motion;},now.AddSeconds(1));Check(!lastMotion,"animation stays disabled during cooldown");
 guard.TryRender(delegate(bool motion){lastMotion=motion;},now.AddSeconds(31));Check(lastMotion,"animation can recover after cooldown");
 var exhausted=new RenderGuard();calls=0;
 Check(!exhausted.TryRender(delegate(bool motion){calls++;throw new ExternalException("synthetic renderer failure");},now),"static rendering failure requests hidden overlay");
 int previous=calls;exhausted.TryRender(delegate(bool motion){calls++;},now.AddMilliseconds(100));Check(calls==previous,"failed rendering does not spin in a rapid retry loop");
 Check(exhausted.TryRender(delegate(bool motion){calls++;},now.AddSeconds(3)),"rendering recovers after retry delay");
 Check(Program.DisplayState(false,new ModeState{Fast=true,FastKnown=true,CapturedAtUtc=now},null,now)=="overlay-hidden","hidden overlay cannot report visible animation");
 Check(Program.DisplayState(true,new ModeState{CapturedAtUtc=now},null,now)=="fast-unknown","unknown Fast state is distinct from off");
 Check(Program.DisplayState(true,new ModeState{Fast=true,FastKnown=true,CapturedAtUtc=now},null,now)=="fast-flow-active","confirmed fresh Fast reports active animation");
 return count;
 }
}
