using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BEN_TOOLBOX_2_0.Services;

public enum PixelPalette { Original, Auto16, Auto32, Auto64, Endesga32, Grayscale16, Custom }
public enum PixelOutline { None, Outside, Inside }
public sealed record PixelArtOptions(int LogicalWidth = 128, bool NearestSource = false,
    PixelPalette Palette = PixelPalette.Original, bool Perceptual = true,
    double Brightness = 1, double Contrast = 1, double Saturation = 1, double Dither = 0, string[]? CustomColours = null,
    double? SamplingBlend = null, double Hue = 0, int AlphaCutoff = 0, int RemoveIslands = 0,
    PixelOutline Outline = PixelOutline.None, int OutlineWidth = 1, string OutlineColour = "#262B44", double OutlineOpacity = 1);

// Shared logical-grid pipeline. Preview can skip enlargement; final output retains the source canvas.
public static class PixelArtService
{
    public static byte[] Preview(string path, PixelArtOptions options, CancellationToken token, bool logicalPreview = false)
    {
        if (options.LogicalWidth is < 16 or > 512) throw new ArgumentException("Detail must be 16–512.");
        if (!double.IsFinite(options.Brightness) || options.Brightness is < 0 or > 2 ||
            !double.IsFinite(options.Contrast) || options.Contrast is < 0 or > 2 ||
            !double.IsFinite(options.Saturation) || options.Saturation is < 0 or > 2 ||
            !double.IsFinite(options.Dither) || options.Dither is < 0 or > 1 || !Enum.IsDefined(options.Palette))
            throw new ArgumentException("Invalid colour settings.");
        double blend = options.SamplingBlend ?? (options.NearestSource ? 1 : 0);
        if (!double.IsFinite(blend) || blend is < 0 or > 1 || !double.IsFinite(options.Hue) || options.Hue is < -180 or > 180 ||
            options.AlphaCutoff is < 0 or > 255 || options.RemoveIslands is < 0 or > 16 || !Enum.IsDefined(options.Outline) || options.OutlineWidth is < 1 or > 4 || !double.IsFinite(options.OutlineOpacity) || options.OutlineOpacity is < 0 or > 1)
            throw new ArgumentException("Invalid sampling/edge settings.");
        var outlineColour = ParseColour(PixelPalettePresets.Validate(new[] { options.OutlineColour })[0]);
        token.ThrowIfCancellationRequested();
        var info = Image.Identify(path) ?? throw new IOException("Unreadable PNG.");
        if (info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 32000000)
            throw new IOException("Preview limit: 8192 per side and 32 million pixels.");
        using var image = Image.Load<Rgba32>(path);
        int width = image.Width, height = image.Height;
        int gridWidth = Math.Min(width, options.LogicalWidth);
        int gridHeight = Math.Clamp((int)Math.Round((double)height * gridWidth / width), 1, height);
        token.ThrowIfCancellationRequested();
        if (blend > 0 && blend < 1)
        {
            using var averaged = image.Clone(x => x.Resize(gridWidth, gridHeight, KnownResamplers.Box));
            image.Mutate(x => x.Resize(gridWidth, gridHeight, KnownResamplers.NearestNeighbor));
            for (int y=0;y<gridHeight;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<gridWidth;x++)
                {
                    var a=averaged[x,y];var b=image[x,y];
                    double alpha=a.A*(1-blend)+b.A*blend;
                    byte Channel(byte ac,byte bc)=>(byte)Math.Clamp(Math.Round(alpha<=0?0:(ac*a.A*(1-blend)+bc*b.A*blend)/alpha),0,255);
                    image[x,y]=new Rgba32(Channel(a.R,b.R),Channel(a.G,b.G),Channel(a.B,b.B),(byte)Math.Round(alpha));
                }
            }
        }
        else image.Mutate(x => x.Resize(gridWidth, gridHeight, blend==1 ? KnownResamplers.NearestNeighbor : KnownResamplers.Box));
        token.ThrowIfCancellationRequested();
        image.Mutate(x => x.Brightness((float)options.Brightness).Contrast((float)options.Contrast).Saturate((float)options.Saturation).Hue((float)options.Hue));
        CleanAlpha(image, options, token);
        ApplyPalette(image, options, token);
        DrawOutline(image, options, outlineColour, token);
        if (!logicalPreview) image.Mutate(x => x.Resize(width, height, KnownResamplers.NearestNeighbor));
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static void CleanAlpha(Image<Rgba32> image, PixelArtOptions options, CancellationToken token)
    {
        int w=image.Width,h=image.Height;
        if(options.AlphaCutoff>0)
            for(int y=0;y<h;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<w;x++){var p=image[x,y];p.A=p.A<options.AlphaCutoff?(byte)0:(byte)255;if(p.A==0)p=default;image[x,y]=p;}
            }
        if(options.RemoveIslands==0)return;
        var visited=new bool[w*h];var connected=new Queue<int>();var component=new List<int>();
        for(int y=0;y<h;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<w;x++)
            {
                int start=y*w+x;if(visited[start]||image[x,y].A==0)continue;
                connected.Enqueue(start);visited[start]=true;component.Clear();int count=0;
                while(connected.Count>0)
                {
                    if((count++&1023)==0)token.ThrowIfCancellationRequested();
                    int index=connected.Dequeue(),cx=index%w,cy=index/w;
                    // Only retain a small component's indices; larger components are preserved.
                    if(component.Count<=options.RemoveIslands)component.Add(index);
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        int nx=cx+dx,ny=cy+dy;if(nx<0||nx>=w||ny<0||ny>=h)continue;
                        int next=ny*w+nx;if(visited[next]||image[nx,ny].A==0)continue;
                        visited[next]=true;connected.Enqueue(next);
                    }
                }
                if(count<=options.RemoveIslands)foreach(int index in component)image[index%w,index/w]=default;
            }
        }
    }
    private static void DrawOutline(Image<Rgba32> image,PixelArtOptions options,Rgba32 colour,CancellationToken token)
    {
        if(options.Outline==PixelOutline.None || options.OutlineOpacity==0)return;
        int w=image.Width,h=image.Height,r=options.OutlineWidth;
        var solid=new bool[w*h];
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)solid[y*w+x]=image[x,y].A>0;
        for(int y=0;y<h;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<w;x++)
            {
                bool inside=options.Outline==PixelOutline.Inside;
                if(solid[y*w+x]!=inside)continue;
                bool edge=false;
                for(int dy=-r;dy<=r&&!edge;dy++)for(int dx=-r;dx<=r;dx++)
                {
                    int nx=x+dx,ny=y+dy;bool neighbour=nx>=0&&nx<w&&ny>=0&&ny<h&&solid[ny*w+nx];
                    if(neighbour!=inside){edge=true;break;}
                }
                if(edge)
                {
                    var p=colour;double opacity=options.OutlineOpacity;
                    if(inside)
                    {
                        var original=image[x,y];
                        p=new Rgba32((byte)Math.Round(original.R*(1-opacity)+colour.R*opacity),
                            (byte)Math.Round(original.G*(1-opacity)+colour.G*opacity),
                            (byte)Math.Round(original.B*(1-opacity)+colour.B*opacity),original.A);
                    }
                    else p.A=(byte)Math.Round(255*opacity);
                    image[x,y]=p;
                }
            }
        }
    }

    private static readonly int[] Bayer = { 0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5 };
    private const string Endesga = "be4a2f d77643 ead4aa e4a672 b86f50 733e39 3e2731 a22633 e43b44 f77622 feae34 fee761 63c74d 3e8948 265c42 193c3e 124e89 0099db 2ce8f5 ffffff c0cbdc 8b9bb4 5a6988 3a4466 262b44 181425 ff0044 68386c b55088 f6757a e8b796 c28569";
    public static string[] EndesgaColours() => Endesga.Split(' ').Select(h => "#" + h.ToUpperInvariant()).ToArray();
    private static Rgba32 ParseColour(string h) => new(Convert.ToByte(h.Substring(1,2),16),Convert.ToByte(h.Substring(3,2),16),Convert.ToByte(h.Substring(5,2),16));
    private static void ApplyPalette(Image<Rgba32> image, PixelArtOptions options, CancellationToken token)
    {
        if (options.Palette == PixelPalette.Original) return;
        Rgba32[] palette = options.Palette switch
        {
            PixelPalette.Custom => PixelPalettePresets.Validate(options.CustomColours ?? Array.Empty<string>()).Select(ParseColour).ToArray(),
            PixelPalette.Endesga32 => Endesga.Split(' ').Select(h => new Rgba32(Convert.ToByte(h[..2],16), Convert.ToByte(h.Substring(2,2),16), Convert.ToByte(h[4..],16))).ToArray(),
            PixelPalette.Grayscale16 => Enumerable.Range(0,16).Select(i => new Rgba32((byte)(i*17),(byte)(i*17),(byte)(i*17))).ToArray(),
            _ => AutomaticPalette(image, options.Palette == PixelPalette.Auto16 ? 16 : options.Palette == PixelPalette.Auto32 ? 32 : 64, token)
        };
        if (palette.Length == 0) return;
        var colours = palette.Select(p => Coordinates(p.R,p.G,p.B,options.Perceptual)).ToArray();
        for (int y=0;y<image.Height;y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x=0;x<image.Width;x++)
            {
                var p=image[x,y]; if(p.A==0) continue;
                double offset = ((Bayer[(y%4)*4+x%4]+0.5)/16-0.5)*options.Dither*64;
                var target=Coordinates(Math.Clamp(p.R+offset,0,255),Math.Clamp(p.G+offset,0,255),Math.Clamp(p.B+offset,0,255),options.Perceptual);
                int best=0; double distance=double.MaxValue;
                for(int i=0;i<colours.Length;i++)
                {
                    var c=colours[i]; double d=(target.x-c.x)*(target.x-c.x)+(target.y-c.y)*(target.y-c.y)+(target.z-c.z)*(target.z-c.z);
                    if(d<distance){distance=d;best=i;}
                }
                var chosen=palette[best]; chosen.A=p.A; image[x,y]=chosen;
            }
        }
    }
    // Weighted median-cut over a bounded 5-bit RGB histogram; fully transparent pixels are excluded.
    private static Rgba32[] AutomaticPalette(Image<Rgba32> image,int count,CancellationToken token)
    {
        var histogram=new Dictionary<int,(long r,long g,long b,long weight)>();
        for(int y=0;y<image.Height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0;x<image.Width;x++)
            {
                var p=image[x,y]; if(p.A==0)continue;
                int key=(p.R>>3)*1024+(p.G>>3)*32+(p.B>>3);
                histogram.TryGetValue(key,out var h);
                histogram[key]=(h.r+p.R*p.A,h.g+p.G*p.A,h.b+p.B*p.A,h.weight+p.A);
            }
        }
        var initial=histogram.OrderBy(h=>h.Key).Select(h=>(r:(double)h.Value.r/h.Value.weight,g:(double)h.Value.g/h.Value.weight,b:(double)h.Value.b/h.Value.weight,w:h.Value.weight)).ToList();
        if(initial.Count==0)return Array.Empty<Rgba32>();
        var boxes=new List<List<(double r,double g,double b,long w)>>{initial};
        while(boxes.Count<count)
        {
            token.ThrowIfCancellationRequested();
            int index=-1,axis=0; double score=-1;
            for(int i=0;i<boxes.Count;i++)
            {
                var box=boxes[i];if(box.Count<2)continue;
                double[] ranges={box.Max(p=>p.r)-box.Min(p=>p.r),box.Max(p=>p.g)-box.Min(p=>p.g),box.Max(p=>p.b)-box.Min(p=>p.b)};
                for(int a=0;a<3;a++)if(ranges[a]>score){score=ranges[a];index=i;axis=a;}
            }
            if(index<0)break;
            var selected=boxes[index].OrderBy(p=>axis==0?p.r:axis==1?p.g:p.b).ToList();
            long half=selected.Sum(p=>p.w)/2, sum=0;int split=0;
            do {sum+=selected[split++].w;}while(split<selected.Count-1&&sum<half);
            boxes[index]=selected.Take(split).ToList();boxes.Add(selected.Skip(split).ToList());
        }
        return boxes.Select(box=>{double w=box.Sum(p=>p.w);return new Rgba32((byte)Math.Round(box.Sum(p=>p.r*p.w)/w),(byte)Math.Round(box.Sum(p=>p.g*p.w)/w),(byte)Math.Round(box.Sum(p=>p.b*p.w)/w));}).ToArray();
    }
    private static (double x,double y,double z) Coordinates(double r,double g,double b,bool perceptual)
    {
        if(!perceptual)return(r/255,g/255,b/255);
        static double Linear(double v){v/=255;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}
        r=Linear(r);g=Linear(g);b=Linear(b);
        double l=Math.Cbrt(0.4122214708*r+0.5363325363*g+0.0514459929*b),
            m=Math.Cbrt(0.2119034982*r+0.6806995451*g+0.1073969566*b),
            s=Math.Cbrt(0.0883024619*r+0.2817188376*g+0.6299787005*b);
        return(0.2104542553*l+0.793617785*m-0.0040720468*s,1.9779984951*l-2.428592205*m+0.4505937099*s,0.0259040371*l+0.7827717662*m-0.808675766*s);
    }
}
