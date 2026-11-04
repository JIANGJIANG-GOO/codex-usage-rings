using System;
using System.Drawing;
using CodexUsageRings;

public static class UltraGradientTests
{
    static int count;
    static readonly DateTime Start=new DateTime(2026,11,4,0,0,0,DateTimeKind.Utc);
    static void Check(bool condition,string message)
    { if(!condition)throw new Exception(message);count++; }
    static Bitmap Frame(double remaining,double seconds,bool ultra,bool fast,bool working,bool motion,float scale)
    {
        DateTime now=Start.AddSeconds(seconds);
        var layout=RingLayout.FromAvatar(new Rectangle(0,100,(int)(24*scale),(int)(24*scale)));
        var image=new Bitmap(layout.Bounds.Width,layout.Bounds.Height);
        using(var g=Graphics.FromImage(image)) {
            g.Clear(Color.Transparent);
            RingPainter.Draw(g,layout,new UsageSnapshot{
                CapturedAtUtc=now,
                Week=new QuotaWindow{UsedPercent=100-remaining,DurationMinutes=10080}
            },now,new ModeState{CapturedAtUtc=now,ModelKnown=true,ModelObservedAtUtc=now,Ultra=ultra,Fast=fast,Working=working},motion);
        }
        return image;
    }
    static bool Same(Bitmap a,Bitmap b,bool alphaOnly)
    {
        for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++) {
            Color p=a.GetPixel(x,y),q=b.GetPixel(x,y);
            if(alphaOnly ? p.A!=q.A : p.ToArgb()!=q.ToArgb())return false;
        }
        return true;
    }
    public static int Run()
    {
        count=0;
        using(var a=Frame(82,0,true,false,false,true,1))
        using(var b=Frame(82,1,true,false,false,true,1))
            Check(!Same(a,b,false),"Ultra gradient flows while idle with Fast off");
        using(var a=Frame(82,0,true,false,false,true,1))
        using(var b=Frame(82,0,true,false,true,true,1))
            Check(Same(a,b,false),"Ultra gradient does not depend on generating state");
        using(var a=Frame(82,0,false,false,false,true,1))
        using(var b=Frame(82,1,false,false,false,true,1))
            Check(Same(a,b,false),"standard effort without Fast remains static");
        using(var a=Frame(82,0,true,true,false,false,1))
        using(var b=Frame(82,1,true,true,false,false,1))
            Check(Same(a,b,false),"render cooldown freezes both Ultra and Fast motion");
        using(var a=Frame(82,.3,true,false,false,true,1))
        using(var b=Frame(82,.3,true,true,false,true,1))
            Check(!Same(a,b,false),"Fast particles remain visible on the Ultra gradient");
        using(var a=Frame(0,0,true,false,false,true,1))
        using(var b=Frame(0,1,false,false,false,true,1))
            Check(Same(a,b,false),"zero quota draws no gradient or false remaining arc");
        foreach(float scale in new[]{1f,1.25f,1.5f,2f})
        foreach(double remaining in new[]{.01,12,50,82,100})
        using(var ultra=Frame(remaining,0,true,false,false,true,scale))
        using(var standard=Frame(remaining,0,false,false,false,true,scale))
            Check(Same(ultra,standard,true),"gradient preserves quota geometry and alpha at "+remaining+" percent / DPI "+scale);
        using(var a=Frame(82,0,true,false,false,true,1))
        using(var b=Frame(82,1,true,false,false,true,1)) {
            bool empty=true,centerSame=true;
            for(int y=40;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y).A!=0)empty=false;
            for(int y=10;y<26;y++)for(int x=10;x<26;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))centerSame=false;
            Check(empty,"Ultra does not reveal an unavailable 5h ring");
            Check(centerSame,"flow stays on the quota stroke and leaves the center label stable");
        }
        bool pale=false,purple=false;
        // At these deterministic field phases, both a pale highlight and saturated
        // purple must occur on the actual stroke, outside the white center label.
        for(int second=0;second<16;second++)
        using(var a=Frame(100,second,true,false,false,true,2)) {
            for(int y=2;y<70;y++)for(int x=2;x<70;x++) {
                double dx=x-36,dy=y-36,r=Math.Sqrt(dx*dx+dy*dy);
                if(r<27 || r>29)continue;
                Color c=a.GetPixel(x,y);
                if(c.A<200)continue;
                pale|=c.R>220 && c.G>210 && c.B>235;
                purple|=c.B>c.G+45 && c.B>c.R+15;
            }
        }
        Check(pale && purple,"Ultra stroke contains both pale white/lavender and purple across its flow");
        return count;
    }
}
