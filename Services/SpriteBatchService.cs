using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record SpriteBatchOptions(string Source, string Destination, string Prefix,
    double Scale, int StartNumber, int Digits, bool LetterSuffixes, bool SpeciesFolders, bool ArchiveOriginals);
public sealed record SpriteBatchEntry(string Source, string Output, int OriginalWidth, int OriginalHeight, int Width, int Height)
{
    public string Display => $"{Path.GetFileName(Source)} → {Path.GetFileName(Output)}  |  {OriginalWidth}×{OriginalHeight} → {Width}×{Height}";
}
public static class SpriteBatchService
{
    public static Task<IReadOnlyList<SpriteBatchEntry>> PlanAsync(SpriteBatchOptions options, CancellationToken ct) =>
        Task.Run(() => Plan(options, ct), ct);
    private static IReadOnlyList<SpriteBatchEntry> Plan(SpriteBatchOptions o, CancellationToken ct)
    {
        if (!Directory.Exists(o.Source) || !Directory.Exists(o.Destination))
            throw new DirectoryNotFoundException("Choose existing source and destination folders.");
        var source = Path.GetFullPath(o.Source);
        var destination = Path.GetFullPath(o.Destination);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked root directories are not supported.");
        static bool Inside(string a,string b) => a.Equals(b,StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(Path.TrimEndingDirectorySeparator(b)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if (Inside(source,destination) || Inside(destination,source)) throw new IOException("Source and destination cannot overlap.");
        if (!double.IsFinite(o.Scale) || o.Scale <= 0 || o.Scale > 1) throw new ArgumentException("Scale must be > 0 and <= 1.");
        if (o.StartNumber < 0 || o.Digits is < 1 or > 8) throw new ArgumentException("Invalid start or digits.");
        if (string.IsNullOrEmpty(o.Prefix) || !System.Text.RegularExpressions.Regex.IsMatch(o.Prefix, "^[A-Za-z][A-Za-z0-9_]*$"))
            throw new ArgumentException("Prefix must start with a letter and contain only ASCII letters, digits, underscores.");
        var files=Directory.EnumerateFiles(source).Where(p=>Path.GetExtension(p).Equals(".png",StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray();
        if(files.Length==0)throw new IOException("No PNG files found.");
        if(!o.LetterSuffixes && (long)o.StartNumber+files.Length-1>int.MaxValue)throw new ArgumentException("Start number too large.");
        var result=new List<SpriteBatchEntry>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<files.Length;i++)
        {
            ct.ThrowIfCancellationRequested();
            int number=o.LetterSuffixes?o.StartNumber:o.StartNumber+i;
            var species=o.Prefix+number.ToString("D"+o.Digits,CultureInfo.InvariantCulture);
            var suffix=o.LetterSuffixes?Letter(i):"";
            var folder=o.SpeciesFolders?Path.Combine(destination,species):destination;
            var target=Path.Combine(folder,species+suffix+".png");
            if(!used.Add(target)||File.Exists(target)||Directory.Exists(target))throw new IOException("Output conflict: "+target);
            if(o.SpeciesFolders && Directory.Exists(folder))throw new IOException("Species folder already exists: "+folder);
            var info=Image.Identify(files[i])??throw new IOException("Invalid PNG: "+files[i]);
            result.Add(new(files[i],target,info.Width,info.Height,
                Math.Max(1,checked((int)Math.Round(info.Width*o.Scale))),
                Math.Max(1,checked((int)Math.Round(info.Height*o.Scale)))));
        }
        return result;
    }
    private static string Letter(int index)
    {
        long n=(long)index+1;string result="";
        while(n>0){n--;result=(char)('a'+n%26)+result;n/=26;}
        return result;
    }
    public static Task<int> ExportAsync(SpriteBatchOptions o,IProgress<string>? status,CancellationToken ct)=>
        Task.Run(()=>Export(o,status,ct),ct);
    private static int Export(SpriteBatchOptions o,IProgress<string>? status,CancellationToken ct)
    {
        var plan=Plan(o,ct);var written=new List<string>();var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? raw=null;
        try
        {
            if(o.ArchiveOriginals)
            {
                raw=Path.Combine(o.Destination,"RAW",Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(raw);folders.Add(raw);
            }
            int completed=0;
            foreach(var entry in plan)
            {
                ct.ThrowIfCancellationRequested();
                var dir=Path.GetDirectoryName(entry.Output)!;
                if(!Directory.Exists(dir)){Directory.CreateDirectory(dir);folders.Add(dir);}
                if(raw!=null)
                {
                    var archived=Path.Combine(raw,Path.GetFileName(entry.Source));
                    File.Copy(entry.Source,archived,false);written.Add(archived);
                }
                using var image=Image.Load<Rgba32>(entry.Source);
                if(entry.Width!=entry.OriginalWidth||entry.Height!=entry.OriginalHeight)
                    image.Mutate(x=>x.Resize(entry.Width,entry.Height,KnownResamplers.Bicubic));
                using(var stream=new FileStream(entry.Output,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    written.Add(entry.Output);
                    image.SaveAsPng(stream);
                }
                status?.Report($"Exported {++completed} of {plan.Count}: {Path.GetFileName(entry.Output)}");
            }
            return completed;
        }
        catch
        {
            var errors=new List<string>();
            foreach(var file in written.AsEnumerable().Reverse())
                try{File.Delete(file);}catch(Exception ex){errors.Add(file+": "+ex.Message);}
            foreach(var folder in folders.OrderByDescending(f=>f.Length))
                try{if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}
                catch(Exception ex){errors.Add(folder+": "+ex.Message);}
            if(errors.Count>0)throw new IOException("Export failed; cleanup incomplete: "+string.Join("; ",errors));
            throw;
        }
    }
}
