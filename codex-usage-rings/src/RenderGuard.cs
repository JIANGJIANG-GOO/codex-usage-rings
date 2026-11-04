using System;using System.Runtime.InteropServices;
namespace CodexUsageRings {
 public sealed class RenderGuard {
  public int FailureCount;
  public string LastErrorType;
  public DateTime LastFailureUtc;
  public DateTime AnimationSuppressedUntilUtc;
  public DateTime RetryAfterUtc;
  public bool MotionAllowed(DateTime now){return now>=AnimationSuppressedUntilUtc;}
  static bool DrawingFailure(Exception e){return e is OutOfMemoryException||e is ExternalException||e is ArgumentException;}
  void Record(Exception e,DateTime now){FailureCount++;LastErrorType=e.GetType().Name;LastFailureUtc=now;AnimationSuppressedUntilUtc=now.AddSeconds(30);}
  public bool TryRender(Action<bool> render,DateTime now){
   if(now<RetryAfterUtc)return false;
   try{render(MotionAllowed(now));return true;}
   catch(Exception e){if(!DrawingFailure(e))throw;Record(e,now);}
   try{render(false);return true;}
   catch(Exception e){if(!DrawingFailure(e))throw;Record(e,now);RetryAfterUtc=now.AddSeconds(2);return false;}
  }
 }
}
