using System;
using System.IO;
using System.Linq;
using System.Threading;
using BEN_TOOLBOX_2_0.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BEN_TOOLBOX_2_0.Services;

// Local procedural sketch, not recovery of the original hidden branches.
public static class BranchPrototypeService
{
    public static bool IsBrown(byte r,byte g,byte b)=>r>g*1.12 && r>b*1.25 && r-g>8;
    public static string SampleWood(SeasonalMaskProject p)
    {
        long r=0,g=0,b=0,count=0;
        for(int i=0;i<p.Automatic.Length;i++)
        {
            int j=i*4;if(p.Rgba[j+3]==0||!IsBrown(p.Rgba[j],p.Rgba[j+1],p.Rgba[j+2]))continue;
            if(SeasonalMaskService.Effective(p,i)>32 && p.Protected[i]==0)continue;
            r+=p.Rgba[j];g+=p.Rgba[j+1];b+=p.Rgba[j+2];count++;
        }
        return count==0?"#73503A":$"#{r/count:X2}{g/count:X2}{b/count:X2}";
    }
    public static byte[] Render(SeasonalMaskProject p,BranchPrototype options,bool generate,CancellationToken token)
    {
        var tips=options.Tips??Array.Empty<BranchPoint>();
        bool Valid(BranchPoint point)=>double.IsFinite(point.X)&&double.IsFinite(point.Y)&&point.X>=0&&point.Y>=0&&point.X<p.Width&&point.Y<p.Height;
        if(tips.Length>16 || tips.Any(t=>t is null||!Valid(t)) || (options.Root is not null&&!Valid(options.Root)) ||
            !double.IsFinite(options.Thickness)||options.Thickness is <1 or >48 || !double.IsFinite(options.Curvature)||options.Curvature is <0 or >1 ||
            options.Forks is <0 or >4 || options.LeafCutoff is <1 or >255 || !double.IsFinite(options.ColourVariation)||options.ColourVariation is <0 or >0.5)
            throw new ArgumentException("Invalid branch prototype settings/points.");
        var text=options.Colour;
        if(text.Length!=7||text[0]!='#')throw new ArgumentException("Branch colour needs #RRGGBB.");
        var colour=new Rgba32(Convert.ToByte(text.Substring(1,2),16),Convert.ToByte(text.Substring(3,2),16),Convert.ToByte(text.Substring(5,2),16));
        if(generate && (options.Root is null || tips.Length==0))throw new ArgumentException("Mark one trunk connection and at least one branch tip first.");
        int w=p.Width,h=p.Height;using var image=new Image<Rgba32>(w,h);var locked=new bool[w*h];
        for(int y=0;y<h;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<w;x++)
            {
                int i=y*w+x,j=i*4;var original=new Rgba32(p.Rgba[j],p.Rgba[j+1],p.Rgba[j+2],p.Rgba[j+3]);
                bool wood=options.PreserveBrown && original.A>0 && IsBrown(original.R,original.G,original.B);
                bool protect=p.Protected[i]>0;
                bool remove=SeasonalMaskService.Effective(p,i)>=options.LeafCutoff && !wood && !protect;
                // All remaining visible source pixels are locked; branches are behind them.
                locked[i]=protect || (original.A>0&&!remove);
                image[x,y]=remove?default:original;
            }
        }
        if(generate)
        {
            var random=new Random(options.Seed);var root=options.Root!;
            var leader=tips.OrderBy(t=>t.Y).ThenBy(t=>Math.Abs(t.X-root.X)).First();
            var leaderControl=Control(root,leader,(random.NextDouble()*2-1)*options.Curvature);
            Draw(root,leaderControl,leader,options.Thickness/2);
            foreach(var tip in tips)
            {
                BranchPoint start,control;
                double radius;
                if(ReferenceEquals(tip,leader)){start=root;control=leaderControl;radius=options.Thickness/2;}
                else
                {
                    double progress=Math.Clamp((root.Y-tip.Y)/Math.Max(1,root.Y-leader.Y)*0.6,0.15,0.8);
                    start=Curve(root,leaderControl,leader,progress);
                    control=Control(start,tip,(random.NextDouble()*2-1)*options.Curvature);
                    radius=options.Thickness/2*(1-progress)*0.75;Draw(start,control,tip,Math.Max(0.7,radius));
                }
                for(int fork=0;fork<options.Forks;fork++)
                {
                    double t=0.45+random.NextDouble()*0.4;var from=Curve(start,control,tip,t);var tangent=Curve(start,control,tip,Math.Min(1,t+0.05));
                    double angle=Math.Atan2(tangent.Y-from.Y,tangent.X-from.X)+(fork%2==0?1:-1)*(0.35+random.NextDouble()*0.6);
                    double length=Math.Sqrt(Math.Pow(tip.X-start.X,2)+Math.Pow(tip.Y-start.Y,2))*(0.15+random.NextDouble()*0.2);
                    var end=new BranchPoint(Math.Clamp(from.X+Math.Cos(angle)*length,0,w-1),Math.Clamp(from.Y+Math.Sin(angle)*length,0,h-1));
                    Draw(from,Control(from,end,options.Curvature*(random.NextDouble()-0.5)),end,Math.Max(0.6,radius*(1-t)*0.65));
                }
            }
        }
        token.ThrowIfCancellationRequested();using var stream=new MemoryStream();image.SaveAsPng(stream);return stream.ToArray();

        void Draw(BranchPoint a,BranchPoint c,BranchPoint b,double baseRadius)
        {
            double length=Distance(a,c)+Distance(c,b);
            int steps=Math.Clamp((int)Math.Ceiling(length/Math.Max(0.75,baseRadius*0.25)),1,8192);
            for(int step=0;step<=steps;step++)
            {
                if((step&31)==0)token.ThrowIfCancellationRequested();double t=(double)step/steps;var point=Curve(a,c,b,t);double radius=Math.Max(0.45,baseRadius*Math.Pow(1-t,0.8));
                for(int y=Math.Max(0,(int)(point.Y-radius));y<=Math.Min(h-1,(int)Math.Ceiling(point.Y+radius));y++)
                    for(int x=Math.Max(0,(int)(point.X-radius));x<=Math.Min(w-1,(int)Math.Ceiling(point.X+radius));x++)
                    {
                        if(locked[y*w+x]||Math.Pow(x-point.X,2)+Math.Pow(y-point.Y,2)>radius*radius)continue;
                        uint hash=unchecked((uint)(x*73856093^y*19349663^options.Seed));hash^=hash>>13;
                        double shade=1+options.ColourVariation*((x-point.X)/Math.Max(1,radius)*0.6+((hash%100)/100d-0.5)*0.6);
                        image[x,y]=new Rgba32((byte)Math.Clamp(Math.Round(colour.R*shade),0,255),(byte)Math.Clamp(Math.Round(colour.G*shade),0,255),(byte)Math.Clamp(Math.Round(colour.B*shade),0,255),255);
                    }
            }
        }
    }
    private static double Distance(BranchPoint a,BranchPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private static BranchPoint Control(BranchPoint a,BranchPoint b,double bend)=>new((a.X+b.X)/2-(b.Y-a.Y)*bend,(a.Y+b.Y)/2+(b.X-a.X)*bend);
    private static BranchPoint Curve(BranchPoint a,BranchPoint c,BranchPoint b,double t)=>new((1-t)*(1-t)*a.X+2*(1-t)*t*c.X+t*t*b.X,(1-t)*(1-t)*a.Y+2*(1-t)*t*c.Y+t*t*b.Y);
}
