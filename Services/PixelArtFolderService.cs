using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record PixelFolderScan(string[] Files, int Skipped);
public static class PixelArtFolderService
{
    public static Task<PixelFolderScan> ScanAsync(string root, bool recursive, CancellationToken token) => Task.Run(() =>
    {
        root=Path.GetFullPath(root);
        if(!Directory.Exists(root) || (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a regular folder.");
        var folders=new Stack<string>();folders.Push(root);
        var files=new List<string>();int skipped=0,visited=0;
        while(folders.Count>0)
        {
            token.ThrowIfCancellationRequested();
            if(++visited>100000)throw new IOException("Folder scan limit exceeded; choose a smaller folder.");
            var folder=folders.Pop();
            try
            {
                foreach(var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attr=File.GetAttributes(entry);
                        if((attr&FileAttributes.ReparsePoint)!=0){skipped++;continue;}
                        if((attr&FileAttributes.Directory)!=0)
                        {
                            if(recursive && !Path.GetFileName(entry).Equals(".pixel-backups",StringComparison.OrdinalIgnoreCase))folders.Push(entry);
                        }
                        else if(Path.GetExtension(entry).Equals(".png",StringComparison.OrdinalIgnoreCase))
                        {
                            if(files.Count>=10000)throw new InvalidOperationException("More than 10,000 PNGs; choose a smaller folder.");
                            files.Add(entry);
                        }
                    }
                    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;}
                }
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){skipped++;}
        }
        return new PixelFolderScan(files.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray(),skipped);
    },token);

    private static byte[] Hash(string path){using var stream=File.OpenRead(path);return SHA256.HashData(stream);}

    // Prepare every conversion before changing any source. Backups persist after success and rollback.
    public static Task<int> OverwriteAsync(PixelArtExportOptions options,IProgress<string>? progress,CancellationToken token)=>Task.Run(async()=>
    {
        if(!options.Overwrite || options.SourceRoot is null)throw new ArgumentException("Overwrite is restricted to folder mode.");
        var plan=await PixelArtExportService.PlanAsync(options,token);
        string job=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
        var staged=new List<(string source,string temporary,string backup,byte[] hash)>();
        var replaced=new List<(string source,string backup)>();
        try
        {
            foreach(var entry in plan)
            {
                token.ThrowIfCancellationRequested();
                string folder=Path.GetDirectoryName(entry.Source)!;
                string backupDir=Path.Combine(folder,".pixel-backups",job);Directory.CreateDirectory(backupDir);
                string temp=Path.Combine(folder,".pixel-stage-"+Guid.NewGuid().ToString("N")+".tmp");
                var hash=Hash(entry.Source);
                var bytes=PixelArtService.Preview(entry.Source,options.Processing,token);
                if(!hash.SequenceEqual(Hash(entry.Source)))throw new IOException("Source changed during conversion: "+entry.Source);
                staged.Add((entry.Source,temp,Path.Combine(backupDir,Path.GetFileName(entry.Source)+".bak"),hash));
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes);
                progress?.Report($"Prepared {staged.Count}/{plan.Length}; sources not yet changed.");
            }
            foreach(var entry in staged)
            {
                token.ThrowIfCancellationRequested();
                if(!entry.hash.SequenceEqual(Hash(entry.source)))throw new IOException("Source changed before replacement: "+entry.source);
                File.Replace(entry.temporary,entry.source,entry.backup);
                replaced.Add((entry.source,entry.backup));
                progress?.Report($"Overwritten {replaced.Count}/{plan.Length}; backup: {entry.backup}");
            }
            token.ThrowIfCancellationRequested();
            return replaced.Count;
        }
        catch(Exception failure)
        {
            var errors=new List<string>();
            foreach(var entry in replaced.AsEnumerable().Reverse())
            {
                string restore=entry.source+".restore-"+Guid.NewGuid().ToString("N")+".tmp";
                try{File.Copy(entry.backup,restore,false);File.Move(restore,entry.source,true);}
                catch(Exception ex){errors.Add(entry.source+": "+ex.Message);}
                finally{try{if(File.Exists(restore))File.Delete(restore);}catch(Exception ex){errors.Add(ex.Message);}}
            }
            if(errors.Count>0)throw new IOException("Overwrite failed; rollback incomplete. Originals remain in .pixel-backups/"+job+". "+string.Join("; ",errors),failure);
            throw;
        }
        finally
        {
            foreach(var entry in staged)try{if(File.Exists(entry.temporary))File.Delete(entry.temporary);}catch(Exception ex){progress?.Report("Temporary cleanup failed: "+ex.Message);}
        }
    },token);
}
