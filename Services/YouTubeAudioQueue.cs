using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public enum AudioCategory { Music, Other }
public sealed class AudioQueueItem
{
    public string Url { get; }
    public AudioCategory Category { get; set; }
    public string State { get; internal set; } = "Waiting";
    public string? Output { get; internal set; }
    public string? Error { get; internal set; }
    public AudioQueueItem(string url, AudioCategory category) { Url=YouTubeAudioService.ValidateUrl(url); Category=category; }
}
public static class YouTubeAudioQueue
{
    public const int MaximumItems=100;
    public static AudioQueueItem[] Parse(string text,AudioCategory category)
    {
        var lines=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(lines.Length==0)throw new ArgumentException("Paste one or more video URLs, one per line.");
        if(lines.Length>MaximumItems)throw new ArgumentException("Add at most 100 links at a time.");
        return lines.Select(line=>new AudioQueueItem(line,category)).ToArray();
    }
    public static string Destination(string root, AudioCategory category)
    {
        if(!Enum.IsDefined(category))throw new ArgumentException("Choose Music or Other.");
        root=YouTubeAudioService.NormalizePath(root);
        if(!Path.IsPathFullyQualified(root)||!Directory.Exists(root))throw new DirectoryNotFoundException("Choose an existing base output folder.");
        var path=Path.Combine(Path.GetFullPath(root),category.ToString());
        // Do not follow category folders redirected outside the selected base.
        if(Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("The category folder is a link. Choose a normal Music/Other subfolder.");
        Directory.CreateDirectory(path);return path;
    }
    public static async Task RunAsync(IReadOnlyList<AudioQueueItem> items,string root,
        Func<AudioQueueItem,string,Action<AudioStatus>,CancellationToken,Task<string>> download,
        Action<AudioQueueItem,AudioStatus> changed,CancellationToken token)
    {
        foreach(var item in items)
        {
            token.ThrowIfCancellationRequested();
            if(item.State=="Complete")continue;
            item.Output=null;item.Error=null;item.State="Resolving";changed(item,new("Resolving"));
            try
            {
                var folder=Destination(root,item.Category);
                item.Output=await download(item,folder,s=>changed(item,s),token);
                item.State="Complete";changed(item,new("Complete",100,item.Output));
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested)
            {item.State="Cancelled";changed(item,new("Cancelled"));throw;}
            catch(Exception ex)
            {item.State="Failed";item.Error=ex.Message;changed(item,new("Failed — "+ex.Message));}
        }
    }
}
