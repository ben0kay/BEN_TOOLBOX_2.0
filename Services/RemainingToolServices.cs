using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public static class ArtworkPromptService
{
    public static readonly string[] Categories = {"Plant","Grass","Tree","Rock","Surface patch","Ore deposit","Alien creature","Structure"};
    public static string Compose(string category,string subject,string shape,string material,string details,string colors,string notes,int variations,bool transparent,bool shadowless)
    {
        if(variations is < 1 or > 12)throw new ArgumentException("Variations must be between 1 and 12.");
        var prompt=new StringBuilder();
        prompt.AppendLine($"Create {variations} cohesive design variation(s) for a 2D isometric science-fiction survival game.");
        prompt.AppendLine($"Asset type: {category}. Subject: {subject}. Shape: {shape}. Material: {material}.");
        prompt.AppendLine($"Details: {details}. Colours: {colors}.");
        prompt.AppendLine("Art direction: believable ecology and materials, distinctive readable silhouettes, high-quality hand-painted game sprite artwork, consistent angled/isometric viewpoint.");
        prompt.AppendLine(transparent?"Use transparent background and isolated asset.":"Use a plain contrasting backdrop suitable for background removal.");
        if(shadowless)prompt.AppendLine("No cast or contact shadow or ground plane; retain internal surface shading.");
        prompt.AppendLine("No text, watermark, border, scenery or presentation frame.");
        if(!string.IsNullOrWhiteSpace(notes))prompt.AppendLine("Additional requirements: "+notes.Trim());
        return prompt.ToString();
    }
}
public sealed record AssetUsageHit(string Source,int Line,string Kind,int Hops)
{
    public string Display=>$"{(Hops==1?"DIRECT":"INDIRECT")} / {Hops} hop(s) • {Source}:{Line} [{Kind}]";
}
public static class AssetUsageService
{
    private static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase)
    {".tres",".tscn",".cs",".gd",".gdshader",".gdshaderinc",".godot",".json",".cfg",".ini",".gdextension",".ps1"};
    private static readonly HashSet<string> Skip=new(StringComparer.OrdinalIgnoreCase)
    {".git",".godot",".vs",".idea","bin","obj","node_modules",".save-pass-backups"};
    private static readonly Regex Resource=new(@"res://[^\s""'\)\],;}]+",RegexOptions.IgnoreCase);
    private static readonly Regex Uid=new(@"uid://[A-Za-z0-9_]+");
    public static Task<List<AssetUsageHit>> SearchAsync(string project,string asset,CancellationToken ct)=>Task.Run(()=>Search(project,asset,ct),ct);
    private static List<AssetUsageHit> Search(string project,string asset,CancellationToken ct)
    {
        var root=Path.GetFullPath(project);
        if(!File.Exists(Path.Combine(root,"project.godot")))throw new IOException("Select folder containing project.godot.");
        var target=Path.IsPathRooted(asset)?Path.GetFullPath(asset):Path.GetFullPath(Path.Combine(root,asset.StartsWith("res://",StringComparison.OrdinalIgnoreCase)?asset[6..]:asset));
        string rootPrefix=Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar;
        if(!target.StartsWith(rootPrefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(target))
            throw new IOException("Choose an existing file inside this Godot project.");
        var reverse=new Dictionary<string,List<(string source,int line,string kind)>>(StringComparer.OrdinalIgnoreCase);
        void Edge(string source,string dest,int line,string kind)
        {
            if(source.Equals(dest,StringComparison.OrdinalIgnoreCase)||!File.Exists(dest))return;
            if(!reverse.TryGetValue(dest,out var list))reverse[dest]=list=new();
            list.Add((source,line,kind));
        }
        var stack=new Stack<string>();stack.Push(root);
        var uid="";
        if(Path.GetExtension(target) is ".tres" or ".tscn")
            uid=Uid.Match(string.Join("\n",File.ReadLines(target).Take(5))).Value;
        else if(File.Exists(target+".uid"))uid=Uid.Match(File.ReadAllText(target+".uid")).Value;
        while(stack.Count>0)
        {
            ct.ThrowIfCancellationRequested();
            foreach(var entry in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                ct.ThrowIfCancellationRequested();
                var flags=File.GetAttributes(entry);
                if((flags&FileAttributes.ReparsePoint)!=0)continue;
                if((flags&FileAttributes.Directory)!=0)
                {if(!Skip.Contains(Path.GetFileName(entry)))stack.Push(entry);continue;}
                if(!Extensions.Contains(Path.GetExtension(entry)))continue;
                try
                {
                    int lineNo=0;
                    foreach(var line in File.ReadLines(entry))
                    {
                        lineNo++;
                        foreach(Match m in Resource.Matches(line))
                        {
                            var raw=m.Value.TrimEnd('.');
                            var resolved=Path.GetFullPath(Path.Combine(root,raw[6..].Replace('/',Path.DirectorySeparatorChar)));
                            if(resolved.StartsWith(rootPrefix,StringComparison.OrdinalIgnoreCase))Edge(entry,resolved,lineNo,"res://");
                        }
                        if(uid.Length>0&&line.Contains(uid,StringComparison.Ordinal))Edge(entry,target,lineNo,"uid://");
                        // ImageFolder can dynamically load PNGs; tag the folder's PNG files as referenced.
                        if(line.Contains("ImageFolder")&&line.Contains("res://"))
                            foreach(Match m in Resource.Matches(line))
                            {
                                var folder=Path.GetFullPath(Path.Combine(root,m.Value[6..].Replace('/',Path.DirectorySeparatorChar)));
                                if(folder.StartsWith(rootPrefix,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(folder))
                                    foreach(var png in Directory.EnumerateFiles(folder,"*.png",SearchOption.TopDirectoryOnly))
                                        Edge(entry,png,lineNo,"ImageFolder");
                            }
                    }
                }
                catch(IOException){/* inaccessible individual text file; scan continues */}
            }
        }
        var found=new List<AssetUsageHit>();
        var queue=new Queue<(string path,int hops)>();queue.Enqueue((target,0));
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){target};
        while(queue.Count>0)
        {
            ct.ThrowIfCancellationRequested();
            var (path,hops)=queue.Dequeue();
            if(!reverse.TryGetValue(path,out var incoming))continue;
            foreach(var (source,line,kind) in incoming)
            {
                if(!seen.Add(source))continue;
                found.Add(new(Path.GetRelativePath(root,source),line,kind,hops+1));
                queue.Enqueue((source,hops+1));
            }
        }
        return found.OrderBy(x=>x.Hops).ThenBy(x=>x.Source,StringComparer.OrdinalIgnoreCase).ToList();
    }
}
public sealed record BackupOptions(string Source,string Destination,string ExcludeFolders,string ExcludeExtensions);
public sealed record BackupFile(string FullPath,string Relative,long Bytes);
public sealed record BackupPlan(IReadOnlyList<BackupFile> Files,long Bytes,int Skipped)
{
    public string Summary=>$"{Files.Count} selected file(s), {Bytes/(1024.0*1024.0):N2} MiB; {Skipped} excluded/link entries.";
}
public static class FolderBackupService
{
    public static Task<BackupPlan> PreviewAsync(BackupOptions o,CancellationToken ct)=>Task.Run(()=>Plan(o,ct),ct);
    private static BackupPlan Plan(BackupOptions o,CancellationToken ct)
    {
        if(!Directory.Exists(o.Source)||!Directory.Exists(o.Destination))throw new DirectoryNotFoundException("Select existing source and destination folders.");
        var source=Path.GetFullPath(o.Source);var dest=Path.GetFullPath(o.Destination);
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0||(File.GetAttributes(dest)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Linked root directories are not allowed.");
        static bool Inside(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase)||a.StartsWith(Path.TrimEndingDirectorySeparator(b)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(Inside(source,dest)||Inside(dest,source))throw new IOException("Source and destination must not overlap.");
        var excluded=o.ExcludeFolders.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extensions=o.ExcludeExtensions.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
            .Select(x=>x.StartsWith('.')?x:"."+x).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stack=new Stack<string>();stack.Push(source);var files=new List<BackupFile>();int skipped=0;long bytes=0;
        while(stack.Count>0)
        {
            ct.ThrowIfCancellationRequested();
            foreach(var entry in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                ct.ThrowIfCancellationRequested();
                var attrs=File.GetAttributes(entry);
                if((attrs&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                if((attrs&FileAttributes.Directory)!=0)
                {if(excluded.Contains(Path.GetFileName(entry)))skipped++;else stack.Push(entry);continue;}
                if(extensions.Contains(Path.GetExtension(entry))){skipped++;continue;}
                var length=new FileInfo(entry).Length;
                files.Add(new(entry,Path.GetRelativePath(source,entry),length));bytes+=length;
            }
        }
        return new(files,bytes,skipped);
    }
    public static Task<string> RunAsync(BackupOptions o,IProgress<string>? progress,CancellationToken ct)=>Task.Run(()=>Run(o,progress,ct),ct);
    private static string Run(BackupOptions o,IProgress<string>? progress,CancellationToken ct)
    {
        var plan=Plan(o,ct);
        var name=Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(o.Source)));
        var dir=Path.Combine(o.Destination,name+"_backup_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        var errors=new List<string>();int copied=0;
        try
        {
            foreach(var file in plan.Files)
            {
                ct.ThrowIfCancellationRequested();
                var target=Path.Combine(dir,file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                try
                {
                    // Never overwrite. Copy rather than move; source files remain untouched.
                    File.Copy(file.FullPath,target,false);
                    copied++;
                }
                catch(IOException ex){errors.Add(file.Relative+": "+ex.Message);}
                progress?.Report($"{copied}/{plan.Files.Count} files copied");
            }
            var summary=$"Status: {(errors.Count==0?"COMPLETE":"PARTIAL")}\nSource: {o.Source}\nSelected: {plan.Summary}\nCopied: {copied}\nErrors: {errors.Count}\n"+string.Join("\n",errors);
            File.WriteAllText(Path.Combine(dir,"Summary.txt"),summary);
            File.WriteAllText(Path.Combine(dir,"BackupSettings.json"),System.Text.Json.JsonSerializer.Serialize(o,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            return dir;
        }
        catch
        {
            // Do not delete partial backups automatically: preserve evidence and copied data.
            File.WriteAllText(Path.Combine(dir,"Summary.txt"),"INCOMPLETE BACKUP — interrupted or failed. Inspect files before relying on this backup.");
            throw;
        }
    }
}
