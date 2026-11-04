using System;using System.Drawing;using CodexUsageRings;
public static class FlowStress {
 public static int Run(){var start=new DateTime(2026,11,4,8,0,0,DateTimeKind.Utc);int frames=0;
 foreach(float scale in new[]{1f,1.25f,1.5f,2f})foreach(double remaining in new[]{82d,12d,.001d,1d,15d,50d,99.99d,100d}){
 var layout=RingLayout.FromAvatar(new Rectangle(0,300,(int)(24*scale),(int)(24*scale)));
 using(var b=new Bitmap(layout.Bounds.Width,layout.Bounds.Height))using(var g=Graphics.FromImage(b))for(int step=0;step<2000;step++){
 var now=start.AddMilliseconds(step);try{
 g.Clear(Color.Transparent);RingPainter.Draw(g,layout,new UsageSnapshot{CapturedAtUtc=now,Week=new QuotaWindow{UsedPercent=100-remaining,DurationMinutes=10080}},now,new ModeState{Fast=true,Ultra=(step%2)==0,ModelKnown=true,ModelObservedAtUtc=now,CapturedAtUtc=now});frames++;
 }catch(Exception ex){Console.WriteLine("FAIL "+ex.GetType().Name+" scale="+scale+" remaining="+remaining+" millisecond="+step+" frames="+frames+"\n"+ex.StackTrace);throw new Exception("Stress rendering failed",ex);}
 }}Console.WriteLine("PASS "+frames+" frames");return frames;}
}
