using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record PixelArtExportOptions(string[] Sources, string Destination, PixelArtOptions Processing,
    bool Sequential = false, string Prefix = "Sprite", int Start = 1, int Digits = 3, string? SourceRoot = null, bool Overwrite = false);
public sealed record PixelArtExportEntry(string Source, string Output, int Width, int Height)
{
    public string Display => $"{Path.GetFileName(Source)} → {Path.GetFileName(Output)} | {Width}×{Height}";
}
public static class PixelArtExportService
{
    public static Task<PixelArtExportEntry[]> PlanAsync(PixelArtExportOptions o, CancellationToken token) => Task.Run(() => Plan(o, token), token);
    private static PixelArtExportEntry[] Plan(PixelArtExportOptions o, CancellationToken token)
    {
        if (!o.Overwrite && !Directory.Exists(o.Destination)) throw new IOException("Choose an existing output folder.");
        if(o.Overwrite && o.SourceRoot is null)throw new ArgumentException("Overwrite requires folder mode.");
        if (o.Sources.Length == 0) throw new IOException("Add at least one PNG.");
        if (o.Start < 0 || o.Digits is < 1 or > 8 || (long)o.Start + o.Sources.Length - 1 > int.MaxValue)
            throw new ArgumentException("Invalid sequence start/digit count.");
        if (o.Sequential && !System.Text.RegularExpressions.Regex.IsMatch(o.Prefix, "^[A-Za-z][A-Za-z0-9_]*$"))
            throw new ArgumentException("Prefix must start with a letter; use letters, digits and underscores.");
        var destination = o.Overwrite ? "" : Path.GetFullPath(o.Destination);
        var sources = new HashSet<string>(o.Sources.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PixelArtExportEntry>();
        for (int i=0;i<o.Sources.Length;i++)
        {
            token.ThrowIfCancellationRequested();
            string source = Path.GetFullPath(o.Sources[i]);
            if (!Path.GetExtension(source).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new IOException("PNG files only.");
            string name = o.Sequential ? o.Prefix + (o.Start+i).ToString("D"+o.Digits, CultureInfo.InvariantCulture)
                : Path.GetFileNameWithoutExtension(source) + "_px" + o.Processing.LogicalWidth;
            if ((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked inputs are not supported.");
            string relative = o.SourceRoot is null ? "" : Path.GetRelativePath(Path.GetFullPath(o.SourceRoot),source);
            if(o.SourceRoot is not null && (Path.IsPathRooted(relative) || relative==".." || relative.StartsWith(".."+Path.DirectorySeparatorChar)))throw new IOException("Input is outside selected folder.");
            string output = o.Overwrite ? source : Path.Combine(destination,Path.GetDirectoryName(relative)??"",name+".png");
            if (!targets.Add(output) || (!o.Overwrite && (sources.Contains(output) || File.Exists(output) || Directory.Exists(output))))
                throw new IOException("Output conflict: " + output + ". Change naming or destination; no files were overwritten.");
            var info = Image.Identify(source) ?? throw new IOException("Unreadable PNG: " + source);
            if (info.Width > 8192 || info.Height > 8192 || (long)info.Width*info.Height > 32000000) throw new IOException("Image exceeds conversion limits: " + source);
            result.Add(new(source, output, info.Width, info.Height));
        }
        return result.ToArray();
    }
    public static Task<int> ExportAsync(PixelArtExportOptions o, IProgress<string>? progress, CancellationToken token) => Task.Run(() =>
    {
        if(o.Overwrite)throw new InvalidOperationException("Use the backed-up folder overwrite service.");
        var plan = Plan(o, token);
        var created = new List<string>();
        try
        {
            foreach (var entry in plan)
            {
                token.ThrowIfCancellationRequested();
                byte[] bytes = PixelArtService.Preview(entry.Source, o.Processing, token);
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Output)!);
                string temporary = Path.Combine(Path.GetDirectoryName(entry.Output)!, ".pixel-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
                    token.ThrowIfCancellationRequested();
                    File.Move(temporary, entry.Output, false); created.Add(entry.Output);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                progress?.Report($"Exported {created.Count}/{plan.Length}: {Path.GetFileName(entry.Output)}");
            }
            token.ThrowIfCancellationRequested();
            return created.Count;
        }
        catch (Exception failure)
        {
            var errors = new List<string>();
            foreach (string path in created) try { File.Delete(path); } catch (Exception ex) { errors.Add(path + ": " + ex.Message); }
            if (errors.Count > 0) throw new IOException("Export failed; cleanup incomplete: " + string.Join("; ",errors), failure);
            throw;
        }
    }, token);
}

// Single receiving window owns this snapshot until its in-flight jobs have stopped.
public sealed class PixelArtHandoffLease : IDisposable
{
    private readonly string folder;
    public string[] Files { get; }
    private bool disposed;
    private PixelArtHandoffLease(string folder, string[] files) { this.folder=folder; Files=files; }
    public static Task<PixelArtHandoffLease> CreateAsync(string[] sources, PixelArtOptions options, IProgress<string>? progress, CancellationToken token) => Task.Run(() =>
    {
        if(sources.Length==0)throw new IOException("Add PNG files before sending.");
        string folder=Path.Combine(Path.GetTempPath(),"BEN_TOOLBOX_2_0","pixel-art-handoff",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var lease=new PixelArtHandoffLease(folder,new string[sources.Length]);
        try
        {
            for(int i=0;i<sources.Length;i++)
            {
                token.ThrowIfCancellationRequested();
                // Ordinal prefix preserves queue order and disambiguates matching original filenames.
                string path=Path.Combine(folder,$"{i:D6}_"+Path.GetFileName(sources[i]));
                var bytes=PixelArtService.Preview(sources[i],options,token);
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes);
                lease.Files[i]=path;progress?.Report($"Preparing Sprite Batch {i+1}/{sources.Length}");
            }
            token.ThrowIfCancellationRequested();return lease;
        }
        catch(Exception failure)
        {
            try { lease.Dispose(); } catch(Exception cleanup) { throw new IOException("Handoff failed; temporary cleanup failed: " + cleanup.Message, failure); }
            throw;
        }
    },token);
    public void Dispose()
    {
        if(disposed)return;
        if(Directory.Exists(folder))Directory.Delete(folder,true);
        disposed=true;
    }
}
