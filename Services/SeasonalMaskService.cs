using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using BEN_TOOLBOX_2_0.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BEN_TOOLBOX_2_0.Services;

public static class SeasonalMaskService
{
    public static SeasonalMaskProject Load(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!Path.GetExtension(path).Equals(".png",StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a PNG sprite.");
        var info=Image.Identify(path)??throw new IOException("Unreadable PNG.");
        if(info.Width>4096 || info.Height>4096 || (long)info.Width*info.Height>SeasonalMaskProject.MaxPixels)throw new IOException("Studio limit: 4096 per side and 4 million pixels.");
        string hash;using(var stream=File.OpenRead(path))hash=Convert.ToHexString(SHA256.HashData(stream));
        using var image=Image.Load<Rgba32>(path);int count=image.Width*image.Height;
        var rgba=new byte[count*4];image.CopyPixelDataTo(rgba);
        using(var stream=File.OpenRead(path))if(!hash.Equals(Convert.ToHexString(SHA256.HashData(stream)),StringComparison.Ordinal))throw new IOException("Source changed while loading. Try again.");
        token.ThrowIfCancellationRequested();
        return new() { SourcePath=Path.GetFullPath(path),SourceHash=hash,Width=image.Width,Height=image.Height,Rgba=rgba,
            Automatic=new byte[count],Added=new byte[count],Removed=new byte[count],Protected=new byte[count] };
    }
    public static void Validate(FoliageDetection d)
    {
        if(!double.IsFinite(d.Hue)||d.Hue is <0 or >360 || !double.IsFinite(d.Tolerance)||d.Tolerance is <1 or >180 ||
            !double.IsFinite(d.MinSaturation)||d.MinSaturation is <0 or >1 || !double.IsFinite(d.MinLight)||d.MinLight is <0 or >1 ||
            d.MinAlpha is <0 or >255 || !double.IsFinite(d.CanopyBias)||d.CanopyBias is <0 or >1 ||
            !double.IsFinite(d.Softness)||d.Softness is <0 or >1 || d.Grow is < -4 or >4 ||d.RemoveIslands is <0 or >16 ||
            !double.IsFinite(d.ColourTolerance)||d.ColourTolerance is <0.01 or >1)throw new ArgumentException("Invalid foliage detection settings.");
        if(d.PickColour is not null)_=Rgb(d.PickColour);
    }
    public static void Validate(SeasonalLook l)
    {
        if(!Enum.IsDefined(l.Season)||!double.IsFinite(l.Strength)||l.Strength is <0 or >1 || !double.IsFinite(l.AutumnProgress)||l.AutumnProgress is <0 or >1 ||
            !double.IsFinite(l.Snow)||l.Snow is <0 or >1 || l.SnowPatch is <1 or >128)throw new ArgumentException("Invalid seasonal preview settings.");
    }
    public static byte[] Detect(SeasonalMaskProject p,FoliageDetection d,CancellationToken token)
    {
        Validate(d);var mask=new byte[p.Width*p.Height];var picked=d.PickColour is null?null:Rgb(d.PickColour);
        for(int y=0;y<p.Height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<p.Width;x++)
            {
                int i=y*p.Width+x,j=i*4;byte alpha=p.Rgba[j+3];if(alpha==0||alpha<d.MinAlpha)continue;
                double r=p.Rgba[j]/255d,g=p.Rgba[j+1]/255d,b=p.Rgba[j+2]/255d;
                double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
                double sat=max==0?0:delta/max,hue=0;
                if(delta>0){hue=max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);if(hue<0)hue+=360;}
                double distance;
                if(picked is not null)distance=Math.Sqrt(Math.Pow(r-picked[0]/255d,2)+Math.Pow(g-picked[1]/255d,2)+Math.Pow(b-picked[2]/255d,2))/Math.Sqrt(3)/d.ColourTolerance;
                else {double diff=Math.Abs(hue-d.Hue);distance=Math.Min(diff,360-diff)/d.Tolerance;}
                if((picked is null && sat<d.MinSaturation)||max<d.MinLight||distance>1)continue;
                double confidence=d.Softness<=0?1:Math.Clamp((1-distance)/d.Softness,0,1);
                confidence*=1-d.CanopyBias*(double)y/Math.Max(1,p.Height-1);
                mask[i]=(byte)Math.Round(confidence*255);
            }
        }
        if(d.Grow!=0)
        {
            var grown=new byte[mask.Length];int radius=Math.Abs(d.Grow);
            for(int y=0;y<p.Height;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<p.Width;x++)
                {
                    byte value=d.Grow>0?(byte)0:(byte)255;
                    for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)
                    {
                        int nx=x+dx,ny=y+dy;byte v=nx>=0&&nx<p.Width&&ny>=0&&ny<p.Height?mask[ny*p.Width+nx]:(byte)0;
                        value=d.Grow>0?Math.Max(value,v):Math.Min(value,v);
                    }
                    grown[y*p.Width+x]=p.Rgba[(y*p.Width+x)*4+3]==0?(byte)0:value;
                }
            }
            mask=grown;
        }
        if(d.RemoveIslands>0)RemoveIslands(mask,p.Width,p.Height,d.RemoveIslands,token);
        return mask;
    }
    private static void RemoveIslands(byte[] mask,int w,int h,int max,CancellationToken token)
    {
        var seen=new bool[mask.Length];var queue=new System.Collections.Generic.Queue<int>();var small=new System.Collections.Generic.List<int>();
        for(int i=0;i<mask.Length;i++)
        {
            if((i&4095)==0)token.ThrowIfCancellationRequested();if(seen[i]||mask[i]==0)continue;
            queue.Enqueue(i);seen[i]=true;small.Clear();int count=0;
            while(queue.Count>0)
            {
                if((count++&4095)==0)token.ThrowIfCancellationRequested();int index=queue.Dequeue();if(small.Count<=max)small.Add(index);
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {int x=index%w+dx,y=index/w+dy;if(x<0||x>=w||y<0||y>=h)continue;int next=y*w+x;if(seen[next]||mask[next]==0)continue;seen[next]=true;queue.Enqueue(next);}
            }
            if(count<=max)foreach(int index in small)mask[index]=0;
        }
    }
    public static byte Effective(SeasonalMaskProject p,int i)
    {
        if(p.Rgba[i*4+3]==0)return 0;
        // Add wins over automatic; Remove subtracts multiplicatively; protection always wins.
        return (byte)Math.Round(Math.Max(p.Automatic[i],p.Added[i])*(1-p.Removed[i]/255d)*(1-p.Protected[i]/255d));
    }
    public static byte[] Render(SeasonalMaskProject p,StudioView view,double overlay,bool showFoliage,bool showProtection,CancellationToken token)
    {
        Validate(p.Look);using var image=new Image<Rgba32>(p.Width,p.Height);
        for(int y=0;y<p.Height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<p.Width;x++)
            {
                int i=y*p.Width+x,j=i*4;byte effective=Effective(p,i);
                var pixel=new Rgba32(p.Rgba[j],p.Rgba[j+1],p.Rgba[j+2],p.Rgba[j+3]);
                if(view is StudioView.AutomaticMask or StudioView.ProtectionMask or StudioView.EffectiveMask)
                {
                    byte v=view==StudioView.AutomaticMask?p.Automatic[i]:view==StudioView.ProtectionMask?p.Protected[i]:effective;
                    image[x,y]=new Rgba32(v,v,v,255);continue;
                }
                if(view==StudioView.Overlay && pixel.A>0)
                {
                    if(showFoliage)pixel=Blend(pixel,new Rgba32(40,245,110),effective/255d*overlay);
                    if(showProtection)pixel=Blend(pixel,new Rgba32(255,70,180),p.Protected[i]/255d*overlay);
                }
                if(view==StudioView.Seasonal && effective>0)
                {
                    var look=p.Look;var graded=pixel;double lum=(pixel.R*0.2126+pixel.G*0.7152+pixel.B*0.0722)/255;
                    double scale=0.25+lum*1.25;
                    Rgba32 target=look.Season switch
                    {
                        StudioSeason.Spring=>new(115,210,65),StudioSeason.Autumn=>new(235,(byte)Math.Round(190-140*look.AutumnProgress),35),
                        StudioSeason.Winter=>new(95,140,150),_=>pixel
                    };
                    if(look.Season!=StudioSeason.Summer)graded=Blend(pixel,new Rgba32((byte)Math.Clamp(target.R*scale,0,255),(byte)Math.Clamp(target.G*scale,0,255),(byte)Math.Clamp(target.B*scale,0,255)),0.78);
                    if(look.Season==StudioSeason.Winter && look.Snow>0)
                    {
                        uint hash=unchecked((uint)((x/look.SnowPatch)*73856093^(y/look.SnowPatch)*19349663^look.Seed*83492791));hash^=hash>>13;hash*=1274126177;
                        bool top=y==0||Effective(p,i-p.Width)<effective/2;
                        double snow=top?look.Snow:((hash%1000)/1000d<look.Snow?look.Snow*0.65:0);
                        graded=Blend(graded,new Rgba32(226,238,248),snow);
                    }
                    pixel=Blend(pixel,graded,effective/255d*look.Strength);
                }
                image[x,y]=pixel;
            }
        }
        using var stream=new MemoryStream();image.SaveAsPng(stream);return stream.ToArray();
    }
    private static Rgba32 Blend(Rgba32 a,Rgba32 b,double t)=>new((byte)Math.Round(a.R*(1-t)+b.R*t),(byte)Math.Round(a.G*(1-t)+b.G*t),(byte)Math.Round(a.B*(1-t)+b.B*t),a.A);
    private static byte[] Rgb(string hex)
    {
        if(hex.Length!=7||hex[0]!='#')throw new ArgumentException("Use #RRGGBB colour.");
        return new[]{Convert.ToByte(hex.Substring(1,2),16),Convert.ToByte(hex.Substring(3,2),16),Convert.ToByte(hex.Substring(5,2),16)};
    }
    public static void ExportMask(SeasonalMaskProject p,string path,CancellationToken token)
    {
        if(Path.GetFullPath(path).Equals(p.SourcePath,StringComparison.OrdinalIgnoreCase))throw new IOException("Cannot replace the source with a mask.");
        SaveNew(path,Render(p,StudioView.EffectiveMask,0,false,false,token),token);
    }
    public static void SaveNew(string path,byte[] data,CancellationToken token)
    {
        if(File.Exists(path))throw new IOException("File exists. Choose a new filename.");
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(data);token.ThrowIfCancellationRequested();File.Move(temporary,path,false);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
