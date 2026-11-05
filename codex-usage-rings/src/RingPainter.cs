using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CodexUsageRings
{
    public static class RingPainter
    {
        public static void Draw(Graphics g, RingLayout layout, UsageSnapshot snapshot, DateTime now)
        { Draw(g,layout,snapshot,now,null); }
        public static void Draw(Graphics g, RingLayout layout, UsageSnapshot snapshot, DateTime now, ModeState mode, bool allowMotion = true, RefillFrame weekRefill=null, RefillFrame fiveRefill=null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            bool purple=mode!=null && mode.ModelFresh(now) && mode.Ultra;
            bool fast=allowMotion && mode!=null && mode.Fresh(now) && mode.Fast;
            RingVisual week=RingVisual.From(snapshot,snapshot==null?null:snapshot.Week,now);
            RingVisual five=RingVisual.From(snapshot,snapshot==null?null:snapshot.FiveHour,now);
            if(!allowMotion){weekRefill=null;fiveRefill=null;}
            week=RefillVisual(week,weekRefill);five=RefillVisual(five,fiveRefill);
            double seconds=allowMotion ? now.TimeOfDay.TotalSeconds : 0;
            using(Brush fill=purple ? CreateUltraBrush(layout.Week,seconds) : null)
                DrawOne(g, layout.Week, "7d", week, layout.Scale,fill);
            if(weekRefill!=null)DrawRefill(g,layout.Week,weekRefill,layout.Scale);
            else if(fast) DrawFlow(g,layout.Week,week,layout.Scale,now);
            if(five.Known) {
                using(Brush fill=purple ? CreateUltraBrush(layout.FiveHour,seconds) : null)
                    DrawOne(g, layout.FiveHour, "5h", five, layout.Scale,fill);
                if(fiveRefill!=null)DrawRefill(g,layout.FiveHour,fiveRefill,layout.Scale);
                else if(fast) DrawFlow(g,layout.FiveHour,five,layout.Scale,now);
            }
        }
        static RingVisual RefillVisual(RingVisual actual,RefillFrame frame){
            if(frame==null||!actual.Known)return actual;
            return new RingVisual{Known=true,Remaining=frame.Remaining,SweepAngle=(float)(frame.Remaining*3.6),Color=frame.Remaining>15?RingVisual.Green:RingVisual.Red,Reason=""};
        }
        static void DrawRefill(Graphics g,RectangleF rect,RefillFrame frame,float scale){
            if(frame.Gathering){
                double end=(frame.Remaining*3.6-90)*Math.PI/180;
                for(int i=0;i<8;i++){
                    double phase=frame.Progress*2.6+i/8.0;phase-=Math.Floor(phase);
                    double ease=1-Math.Pow(1-phase,2);
                    PointF point=RefillParticlePoint(rect,end,(float)((4+Seed(i,62)*3.5)*scale*(1-ease)));
                    float x=point.X,y=point.Y;
                    float size=(float)(1.2+Seed(i,63)*.9)*scale;
                    int alpha=(int)(210*Math.Sin(Math.PI*phase)*Math.Min(1,(1-frame.Progress)*8));
                    using(var dot=new SolidBrush(Color.FromArgb(Math.Max(0,alpha),85,236,164)))g.FillEllipse(dot,x-size/2,y-size/2,size,size);
                }
            }
            if(frame.Flash>0)using(var flash=new Pen(Color.FromArgb((int)(90*frame.Flash),255,255,255),StrokeWidth*scale))g.DrawEllipse(flash,rect);
        }
        internal static PointF RefillParticlePoint(RectangleF rect,double angle,float distance){
            // Approach the arc endpoint along its tangent, entirely outside the
            // circle. No radial interpolation through the center or label.
            float cosine=(float)Math.Cos(angle),sine=(float)Math.Sin(angle),r=rect.Width/2;
            return new PointF(rect.Left+r+cosine*r+sine*distance,rect.Top+rect.Height/2+sine*r-cosine*distance);
        }
        public static readonly Color Purple=Color.FromArgb(177,115,255);
        public const float StrokeWidth=4f;
        public static void DrawOne(Graphics g, RectangleF rect, string label, RingVisual visual, float scale)
        { DrawOne(g,rect,label,visual,scale,null); }
        static void DrawOne(Graphics g, RectangleF rect, string label, RingVisual visual, float scale,Brush quotaBrush)
        {
            using (Brush background = new SolidBrush(Color.FromArgb(215, 19, 21, 24))) g.FillEllipse(background, rect);
            Color track = visual.Known && visual.Remaining <= 15 ? Color.FromArgb(80, RingVisual.Red) : Color.FromArgb(160, 87, 92, 101);
            using (Pen pen = new Pen(track, StrokeWidth * scale))
            {
                if (!visual.Known) pen.DashStyle = DashStyle.Dot;
                g.DrawEllipse(pen, rect);
            }
            if (visual.Known && visual.SweepAngle > 0)
            {
                if(quotaBrush!=null) FillQuotaArc(g,rect,quotaBrush,StrokeWidth*scale,visual.SweepAngle);
                else using(var fill=new SolidBrush(visual.Color))
                    FillQuotaArc(g,rect,fill,StrokeWidth*scale,visual.SweepAngle);
            }

            using (Font font = new Font("Segoe UI", 11f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush ink = new SolidBrush(visual.Known ? Color.FromArgb(245, 247, 250) : Color.FromArgb(155, 160, 169)))
            using (GraphicsPath glyph=new GraphicsPath())
            {
                glyph.AddString(label,font.FontFamily,(int)font.Style,font.Size,PointF.Empty,StringFormat.GenericTypographic);
                RectangleF inkBounds=glyph.GetBounds();
                using(Matrix transform=new Matrix()) {
                    transform.Translate(rect.Left+rect.Width/2-inkBounds.Left-inkBounds.Width/2,rect.Top+rect.Height/2-inkBounds.Top-inkBounds.Height/2);
                    glyph.Transform(transform);
                }
                g.FillPath(ink,glyph);
            }
        }
        // Fill geometry directly: GDI+ DrawArc with rounded caps throws a spurious
        // OutOfMemoryException for some short arcs on .NET Framework 4.8.
        static void FillQuotaArc(Graphics g,RectangleF rect,Brush fill,float width,float sweep)
        {
            if(sweep<=0 || rect.Width<=0 || rect.Height<=0 || width<=0)return;
            if(sweep>=359.99f) { using(var pen=new Pen(fill,width))g.DrawEllipse(pen,rect);return; }
            int segments=Math.Max(1,(int)Math.Ceiling(sweep/2));
            var polygon=new PointF[(segments+1)*2];
            float cx=rect.Left+rect.Width/2,cy=rect.Top+rect.Height/2;
            float rx=rect.Width/2,ry=rect.Height/2,half=width/2;
            for(int i=0;i<=segments;i++) {
                double angle=(-90+sweep*i/segments)*Math.PI/180;
                float dx=(float)Math.Cos(angle),dy=(float)Math.Sin(angle);
                polygon[i]=new PointF(cx+dx*(rx+half),cy+dy*(ry+half));
                polygon[polygon.Length-1-i]=new PointF(cx+dx*Math.Max(0,rx-half),cy+dy*Math.Max(0,ry-half));
            }
            g.FillPolygon(fill,polygon);
            g.FillEllipse(fill,cx-half,cy-ry-half,width,width);
            double end=(sweep-90)*Math.PI/180;
            g.FillEllipse(fill,cx+(float)Math.Cos(end)*rx-half,cy+(float)Math.Sin(end)*ry-half,width,width);
        }
        // The installed Codex MaxFill shader uses twelve moving Gaussian color fields
        // (impl-12731b6084c8.js, fieldColor; speed 1.25). Sample its center lane into
        // a smooth brush for this small stroke rather than running a WebGL surface.
        // Keep its purple palette and motion; lift the pale fields toward white so
        // the requested purple/white transition is legible on a 4-DIP quota ring.
        static readonly double[,] UltraPalette={
            {.592,.388,.945},{.831,.710,.953},{.286,.000,.404},{.145,.055,.478},
            {.592,.000,.996},{.780,.459,.914},{.725,.576,1.000},{.400,.212,.820},
            {.882,.690,1.000},{.498,.345,.957},{.659,.275,.910},{.212,.063,.400}
        };
        static readonly double[,] UltraFields={
            {.18,.18,.42,.36,.42,.50,.91},{.34,.24,.62,.62,.38,.47,1.07},
            {.52,.28,.38,.30,.36,.58,.76},{.70,.24,.54,.68,.36,.41,1.18},
            {.88,.16,.74,.36,.40,.64,1.03},{.12,.20,.48,.72,.30,.70,.83},
            {.30,.22,.58,.44,.42,.52,1.24},{.46,.26,.68,.72,.32,.56,.96},
            {.60,.28,.44,.26,.38,.60,1.11},{.76,.22,.50,.54,.40,.66,.72},
            {.92,.15,.70,.66,.30,.46,1.29},{.06,.14,.40,.32,.40,.60,.88}
        };
        static Brush CreateUltraBrush(RectangleF rect,double seconds)
        {
            const int stops=33;
            var blend=new ColorBlend(stops);
            var centers=new double[12,3];
            for(int field=0;field<12;field++) {
                bool even=(field%2)==0;
                double tx=seconds*1.25*UltraFields[field,2],ty=seconds*1.25*UltraFields[field,5];
                centers[field,0]=(UltraFields[field,0]+UltraFields[field,1]*(even?Math.Sin(tx):Math.Cos(tx)))*1.55;
                centers[field,1]=UltraFields[field,3]+UltraFields[field,4]*(even?Math.Cos(ty):Math.Sin(ty));
                centers[field,2]=.7+.3*(even?Math.Sin(seconds*UltraFields[field,6]):Math.Cos(seconds*UltraFields[field,6]));
            }
            for(int stop=0;stop<stops;stop++) {
                double x=(double)stop/(stops-1),total=0,pale=0,r=0,g=0,b=0;
                for(int field=0;field<12;field++) {
                    double dx=x*1.55-centers[field,0],dy=.49-centers[field,1];
                    double weight=Math.Exp(-(dx*dx+dy*dy)*12)*centers[field,2];
                    total+=weight;r+=UltraPalette[field,0]*weight;g+=UltraPalette[field,1]*weight;b+=UltraPalette[field,2]*weight;
                    if(field==1 || field==8)pale+=weight;
                }
                total=Math.Max(total,.0001);
                double white=Math.Max(0,Math.Min(1,(pale/total-.2)/.55));
                white=white*white*(3-2*white)*.85;
                r=UltraChannel(r/total,UltraPalette[3,0],white,Math.Sin(seconds*.5)*.02);
                g=UltraChannel(g/total,UltraPalette[3,1],white,Math.Cos(seconds*.7)*.02);
                b=UltraChannel(b/total,UltraPalette[3,2],white,Math.Sin(seconds*.6)*.02);
                blend.Positions[stop]=(float)x;
                blend.Colors[stop]=Color.FromArgb((int)Math.Round(r*255),(int)Math.Round(g*255),(int)Math.Round(b*255));
            }
            // Include both stroke edges so the brush never wraps at an outer pixel.
            float margin=rect.Width*StrokeWidth/28f;
            var brush=new LinearGradientBrush(new PointF(rect.Left-margin,rect.Top),new PointF(rect.Right+margin,rect.Top),Color.Black,Color.White);
            brush.InterpolationColors=blend;
            return brush;
        }
        static double UltraChannel(double channel,double baseChannel,double white,double drift)
        {
            channel=Math.Pow(Math.Max(0,Math.Min(1,baseChannel*.04+channel*.96)),.9);
            channel=Math.Pow(Math.Max(0,Math.Min(1,channel+drift)),.92);
            return channel+(1-channel)*white;
        }
        static double Seed(int index,int salt)
        {
            double value=Math.Sin((index+1)*12.9898+salt*78.233)*43758.5453;
            return value-Math.Floor(value);
        }
        static void DrawFlow(Graphics g,RectangleF rect,RingVisual visual,float scale,DateTime now)
        {
            if(!visual.Known || visual.SweepAngle<=0)return;
            double sweep=visual.SweepAngle,seconds=now.TimeOfDay.TotalSeconds;
            int particles=Math.Max(2,Math.Min(3,(int)Math.Ceiling(sweep/110)));
            // Based on the installed Codex FastTrackParticleTravel motion:
            // linear travel, 1.9s duration with +/-20% variation, staggered starts,
            // independently varied opacity/size/lane, 8% fade-in and fade-out.
            // Circular adaptation: reduced particle count; official size, white ink and soft shadow.
            for(int point=0;point<particles;point++) {
                double duration=1.9/(1+(Seed(point,21)-.5)*.4);
                double phase=(seconds-point*1.9/particles)/duration;
                phase-=Math.Floor(phase);
                double fade=Math.Min(1,Math.Min(phase/.08,(1-phase)/.08));
                int alpha=(int)(184*(.4+Seed(point,11)*.6)*fade);
                if(alpha<1)continue;
                float diameter=(float)(3*(.5+Seed(point,12)*.45))*scale;
                float lane=(float)((.12+Seed(point,23)*.76-.5)*(StrokeWidth*scale-diameter));
                double angle=(-90+(1-phase)*sweep)*Math.PI/180;
                float x=rect.Left+rect.Width/2+(float)Math.Cos(angle)*(rect.Width/2+lane);
                float y=rect.Top+rect.Height/2+(float)Math.Sin(angle)*(rect.Height/2+lane);
                // Approximate the official 5px white CSS shadow with a soft radial falloff.
                float halo=diameter+5*scale;
                using(var path=new GraphicsPath()) {
                    path.AddEllipse(x-halo/2,y-halo/2,halo,halo);
                    using(var glow=new PathGradientBrush(path)) {
                        glow.CenterPoint=new PointF(x,y);
                        glow.CenterColor=Color.FromArgb((int)(87*(.4+Seed(point,11)*.6)*fade),255,255,255);
                        glow.SurroundColors=new[]{Color.FromArgb(0,255,255,255)};
                        g.FillPath(glow,path);
                    }
                }
                using(var particle=new SolidBrush(Color.FromArgb(alpha,255,255,255)))
                    g.FillEllipse(particle,x-diameter/2,y-diameter/2,diameter,diameter);
            }
        }

        public static string Describe(string label, UsageSnapshot snapshot, QuotaWindow quota, DateTime now)
        {
            RingVisual visual = RingVisual.From(snapshot, quota, now);
            string text = label + "：" + (visual.Known ? "剩余 " + visual.Remaining.ToString("0.#") + "%（已用 " + quota.UsedPercent.ToString("0.#") + "%）" : visual.Reason);
            if (quota != null && quota.ResetsAtUtc.HasValue) text += "\n重置：" + quota.ResetsAtUtc.Value.ToLocalTime().ToString("MM-dd HH:mm:ss");
            if (snapshot != null)
            {
                text += "\n来源：" + snapshot.Source;
                if (snapshot.CapturedAtUtc != DateTime.MinValue) text += "\n采样：" + snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                if (snapshot.Source != "app-server") text += "\n" + snapshot.Status;
            }
            return text;
        }
    }
}
