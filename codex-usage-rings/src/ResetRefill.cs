using System;
namespace CodexUsageRings {
 public sealed class RefillFrame {
  public double Remaining,Progress,Flash;public bool Gathering;
 }
 public sealed class ResetRefill {
  public const double FillMilliseconds=1400,FlashMilliseconds=350;
  UsageSnapshot before;DateTime acceptedAt;long armedAt,startedAt;bool pending,playing;
  double weekFrom,weekTo,fiveFrom,fiveTo;bool week,five;
  public void Arm(UsageSnapshot prior,DateTime accepted,long milliseconds){before=prior;acceptedAt=accepted;armedAt=milliseconds;pending=true;playing=false;week=five=false;}
  public void Observe(UsageSnapshot current,DateTime now,long milliseconds){
   if(!pending)return;
   if(milliseconds<armedAt||milliseconds-armedAt>30000){pending=false;return;}
   if(current==null||current.Source!="app-server"||current.CapturedAtUtc<=acceptedAt)return;
   var oldWeek=RingVisual.From(before,before==null?null:before.Week,acceptedAt);var newWeek=RingVisual.From(current,current.Week,now);
   var oldFive=RingVisual.From(before,before==null?null:before.FiveHour,acceptedAt);var newFive=RingVisual.From(current,current.FiveHour,now);
   week=oldWeek.Known&&newWeek.Known&&newWeek.Remaining>oldWeek.Remaining+.01;
   five=oldFive.Known&&newFive.Known&&newFive.Remaining>oldFive.Remaining+.01;
   if(!week&&!five)return;
   weekFrom=oldWeek.Remaining;weekTo=newWeek.Remaining;fiveFrom=oldFive.Remaining;fiveTo=newFive.Remaining;
   startedAt=milliseconds;playing=true;pending=false;before=null;
  }
  public RefillFrame Sample(bool fiveHour,long milliseconds){
   if(!playing||!(fiveHour?five:week))return null;
   double elapsed=milliseconds-startedAt;if(elapsed<0||elapsed>=FillMilliseconds+FlashMilliseconds)return null;
   double from=fiveHour?fiveFrom:weekFrom,to=fiveHour?fiveTo:weekTo;
   double t=Math.Min(1,elapsed/FillMilliseconds),ease=t*t*(3-2*t);
   double flash=elapsed>=FillMilliseconds&&to>=99.99?Math.Sin(Math.PI*(elapsed-FillMilliseconds)/FlashMilliseconds):0;
   return new RefillFrame{Remaining=from+(to-from)*ease,Progress=t,Gathering=t<1,Flash=flash};
  }
 }
}
