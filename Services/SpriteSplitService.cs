using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record SpriteRegion(int X, int Y, int Width, int Height);

public static class SpriteSplitService
{
    public static (int Width, int Height, List<int> Cuts) Detect(string file, byte threshold, int minimumGap)
    {
        using var image = Image.Load<Rgba32>(file);
        var cuts = new List<int>();
        int gapStart = -1;
        bool seen = false;
        for (int x = 0; x < image.Width; x++)
        {
            bool filled = false;
            for (int y = 0; y < image.Height; y++)
                if (image[x, y].A > threshold) { filled = true; break; }
            if (!filled)
            {
                if (gapStart < 0) gapStart = x;
            }
            else
            {
                if (seen && gapStart >= 0 && x - gapStart >= minimumGap)
                    cuts.Add(gapStart + (x-gapStart)/2);
                gapStart = -1;
                seen = true;
            }
        }
        return (image.Width,image.Height,cuts);
    }

    public static List<SpriteRegion> Regions(string path, IEnumerable<int> cuts, byte threshold)
    {
        using var image = Image.Load<Rgba32>(path);
        var edges = new List<int> { 0 };
        edges.AddRange(cuts.Where(x => x > 0 && x < image.Width).Distinct().OrderBy(x=>x));
        edges.Add(image.Width);
        var result = new List<SpriteRegion>();
        for (int i = 0; i + 1 < edges.Count; i++)
        {
            int minX=image.Width,minY=image.Height,maxX=-1,maxY=-1;
            for (int y=0;y<image.Height;y++)
                for (int x=edges[i];x<edges[i+1];x++)
                    if (image[x,y].A > threshold)
                    {
                        minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);
                        minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    }
            if (maxX>=minX)
                result.Add(new SpriteRegion(minX,minY,maxX-minX+1,maxY-minY+1));
        }
        return result;
    }

    // mode: 0 individual crop, 1 equal-size rectangle, 2 equal-size square
    public static IReadOnlyList<string> Export(string path, string destination, string prefix, IEnumerable<int> cuts,
        byte threshold, int padding, int mode)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.IndexOfAny(Path.GetInvalidFileNameChars())>=0 ||
            prefix=="." || prefix=="..")
            throw new ArgumentException("Enter a valid filename prefix.");
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("Destination does not exist.");
        if (padding<0 || mode<0 || mode>2) throw new ArgumentException("Invalid canvas settings.");
        using var image=Image.Load<Rgba32>(path);
        var regions=Regions(path,cuts,threshold);
        if (regions.Count==0) throw new InvalidOperationException("No opaque sprites found.");
        int width=regions.Max(r=>r.Width)+2*padding, height=regions.Max(r=>r.Height)+2*padding;
        if(mode==2) width=height=Math.Max(width,height);
        var output=new List<(string Path,Image<Rgba32> Bitmap)>();
        try
        {
            for(int i=0;i<regions.Count;i++)
            {
                var region=regions[i];
                int w=mode==0?region.Width+2*padding:width;
                int h=mode==0?region.Height+2*padding:height;
                string name=$"{prefix}_{i+1:D2}.png";
                string target=Path.Combine(destination,name);
                if(File.Exists(target)) throw new IOException("File already exists: "+target);
                using var cropped=image.Clone(ctx=>ctx.Crop(new Rectangle(region.X,region.Y,region.Width,region.Height)));
                var outputImage=new Image<Rgba32>(w,h,Color.Transparent);
                outputImage.Mutate(ctx=>ctx.DrawImage(cropped,new Point((w-region.Width)/2,h-padding-region.Height),1f));
                output.Add((target,outputImage));
            }
            var written=new List<string>();
            try
            {
                foreach(var item in output)
                {
                    using var fs=new FileStream(item.Path,FileMode.CreateNew,FileAccess.Write);
                    written.Add(item.Path);
                    item.Bitmap.SaveAsPng(fs);
                }
            }
            catch
            {
                foreach(var p in written) { try { File.Delete(p); } catch { } }
                throw;
            }
            return written;
        }
        finally { foreach(var item in output) item.Bitmap.Dispose(); }
    }
}
