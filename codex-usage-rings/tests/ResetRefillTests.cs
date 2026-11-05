using System;using System.Drawing;using CodexUsageRings;
public static class ResetRefillTests {
 static int count;static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static UsageSnapshot Quota(DateTime at,double used){return new UsageSnapshot{Source="app-server",CapturedAtUtc=at,Week=new QuotaWindow{UsedPercent=used,DurationMinutes=10080}};}
 public static int Run(){count=0;var now=DateTime.UtcNow;var anim=new ResetRefill();var old=Quota(now.AddSeconds(-1),80);var fresh=Quota(now.AddSeconds(1),0);
  anim.Observe(fresh,now.AddSeconds(1),100);Check(anim.Sample(false,100)==null,"ordinary quota updates do not animate");
  anim.Arm(old,now,0);anim.Observe(Quota(now.AddSeconds(-1),0),now,100);Check(anim.Sample(false,100)==null,"pre-reset response cannot trigger fill");
  anim.Observe(Quota(now.AddSeconds(1),80),now.AddSeconds(1),200);Check(anim.Sample(false,200)==null,"unchanged quota waits");
  anim.Observe(fresh,now.AddSeconds(1),300);Check(Math.Abs(anim.Sample(false,300).Remaining-20)<.001,"start at actual prior quota");
  Check(anim.Sample(true,400)==null,"missing 5h never fabricated");double prior=20;
  for(int t=300;t<=1700;t+=50){var frame=anim.Sample(false,t);Check(frame.Remaining>=prior&&frame.Remaining<=100,"monotonic bounded fill");prior=frame.Remaining;}
  Check(anim.Sample(false,1700).Remaining==100&&!anim.Sample(false,1700).Gathering,"fill completes before flash");
  Check(anim.Sample(false,1875).Flash>.99,"white pulse peaks after fill");Check(anim.Sample(false,2050)==null,"effect ends cleanly");
  Check(old.Week.UsedPercent==80&&fresh.Week.UsedPercent==0,"animation never changes real quota snapshots");
  var partial=new ResetRefill();partial.Arm(old,now,0);partial.Observe(Quota(now.AddSeconds(1),15),now.AddSeconds(1),100);
  Check(partial.Sample(false,1675).Remaining==85&&partial.Sample(false,1675).Flash==0,"partial recovery does not pretend full or flash");
  var expired=new ResetRefill();expired.Arm(old,now,0);expired.Observe(fresh,now.AddSeconds(31),31001);Check(expired.Sample(false,31001)==null,"old reset event expires");
  var layout=RingLayout.FromAvatar(new Rectangle(0,100,24,24));
  for(int i=0;i<180;i++)using(var bitmap=new Bitmap(36,76))using(var graphics=Graphics.FromImage(bitmap)){
   RingPainter.Draw(graphics,layout,fresh,now.AddSeconds(1),new ModeState{Ultra=i%2==0,ModelKnown=true,ModelObservedAtUtc=now,Fast=true,CapturedAtUtc=now},true,anim.Sample(false,300+i*10),null);
  }
  Check(true,"180 refill frames including Ultra and Fast render safely");
  var circle=new RectangleF(4,4,28,28);
  for(int degrees=0;degrees<360;degrees+=30){double angle=degrees*Math.PI/180;var end=RingPainter.RefillParticlePoint(circle,angle,0);var start=RingPainter.RefillParticlePoint(circle,angle,7.5f);
   double nx=Math.Cos(angle),ny=Math.Sin(angle),dx=start.X-end.X,dy=start.Y-end.Y;
   Check(Math.Abs(dx*nx+dy*ny)<.00001,"particle path is tangent to endpoint");
   Check(Math.Pow(start.X-18,2)+Math.Pow(start.Y-18,2)>=196,"particle origin stays outside circle");
  }
  return count;
 }
}
