using System;
using System.Drawing;
using CodexUsageRings;
public static class EnhancementTests
{
    static int count;
    static void Check(bool value,string why) { if(!value) throw new Exception(why); count++; }
    public static int Run()
    {
        count=0; DateTime now=DateTime.UtcNow;
        foreach(float scale in new[]{1f,1.25f,1.5f,2f}) {
            var geometry=RingLayout.FromAvatar(new Rectangle(0,100,(int)(24*scale),(int)(24*scale)));
            Check(Math.Abs(geometry.Week.Width-RingPainter.StrokeWidth*geometry.Scale-24*scale)<.01,"inner diameter equals avatar at DPI "+scale);
            using(Bitmap image=new Bitmap(100,100)) using(Graphics g=Graphics.FromImage(image)) {
                g.Clear(Color.Transparent);
                RectangleF rect=new RectangleF(4*scale,4*scale,28*scale,28*scale);
                RingPainter.DrawOne(g,rect,"7d",new RingVisual{Known=true,Remaining=50,SweepAngle=180,Color=RingVisual.Green},scale);
                int left=100,top=100,right=-1,bottom=-1;
                for(int y=0;y<100;y++) for(int x=0;x<100;x++) { Color c=image.GetPixel(x,y); if(c.A>180 && c.R>190 && c.G>190 && c.B>190) { left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y); } }
                Check(right>=left,"label visible");
                Check(Math.Abs((left+right+1)/2f-(rect.Left+rect.Width/2))<=.6f && Math.Abs((top+bottom+1)/2f-(rect.Top+rect.Height/2))<=.6f,"actual glyph ink centered at DPI "+scale);
            }
        }
        RingLayout layout=RingLayout.FromAvatar(new Rectangle(0,100,24,24));
        using(Bitmap image=new Bitmap(36,76)) using(Graphics g=Graphics.FromImage(image)) {
            g.Clear(Color.Transparent);
            RingPainter.Draw(g,layout,new UsageSnapshot{CapturedAtUtc=now,Week=new QuotaWindow{UsedPercent=20,DurationMinutes=10080}},now);
            bool any=false; for(int y=40;y<76;y++) for(int x=0;x<36;x++) if(image.GetPixel(x,y).A!=0) any=true;
            Check(!any,"missing 5h must draw nothing");
        }
        var m=ModeDetector.FromLabels(new[]{"GPT-6 Astra Ultra","Stop","Enable standard mode"},now);
        Check(m.Ultra && m.Fast && m.Working,"active astra ultra fast recognized");
        m=ModeDetector.FromLabels(new[]{"GPT-6 Astra Medium","Stop","Enable fast mode"},now);
        Check(!m.Ultra && !m.Fast,"medium and available-enable button not active modes");
        m=ModeDetector.FromLabels(new[]{"GPT-6 Astra Ultra","[off] Fast"},now);
        Check(!m.Working && !m.Fast,"idle and disabled fast remain inactive");
        m=ModeDetector.FromLabels(new[]{"Running echo astra ultra fast","Stop"},now);
        Check(!m.Ultra && !m.Fast,"tool output cannot select modes");
        return count;
    }
}
