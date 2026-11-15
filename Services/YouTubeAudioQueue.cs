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
    public bool IsPlaylist { get; }
    public AudioCategory Category { get; set; }
    public string State { get; internal set; } = "Waiting";
    public string? Output { get; internal set; }
    public string? Title { get; internal set; }
    public string? Error { get; internal set; }
    internal bool IsUnavailable { get; }

    public AudioQueueItem(string url, AudioCategory category)
    {
        (Url,IsPlaylist)=YouTubeAudioService.ClassifyUrl(url);
        Category=category;
    }

    private AudioQueueItem(string url,AudioCategory category,string? title,bool unavailable)
    {
        Url=url;
        Category=category;
        Title=title;
        IsUnavailable=unavailable;
        if(unavailable)
        {
            State="Failed";
            Error="Playlist entry is unavailable or has no supported track URL.";
        }
    }

    internal static AudioQueueItem PlaylistTrack(string url,AudioCategory category,string? title) =>
        new(url,category,title,false);

    internal static AudioQueueItem UnavailableTrack(AudioQueueItem playlist,string? title) =>
        new(playlist.Url,playlist.Category,title??"Unavailable playlist entry",true);
}
public static class YouTubeAudioQueue
{
    public const int MaximumItems=100;
    public static AudioQueueItem[] Parse(string text,AudioCategory category)
    {
        var lines=text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(lines.Length==0)throw new ArgumentException("Paste YouTube or SoundCloud track or playlist links, one per line.");
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
    public static async Task RunAsync(IList<AudioQueueItem> items,string root,
        Func<AudioQueueItem,string,Action<AudioStatus>,CancellationToken,Task<string?>> download,
        Action<AudioQueueItem,AudioStatus> changed,CancellationToken token,
        Func<AudioQueueItem,int,CancellationToken,Task<IReadOnlyList<AudioQueueItem>>>? expand=null,
        Action? queueChanged=null)
    {
        for(int index=0;index<items.Count;index++)
        {
            token.ThrowIfCancellationRequested();
            var item=items[index];
            if(item.IsUnavailable || item.State is "Complete" or "Skipped")continue;
            item.Output=null;item.Error=null;item.State="Resolving";changed(item,new("Resolving"));
            try
            {
                if(item.IsPlaylist)
                {
                    if(expand is null) throw new InvalidOperationException("Playlist expansion is unavailable.");
                    changed(item,new("Resolving — expanding playlist"));
                    int availableSlots=MaximumItems-items.Count+1;
                    var tracks=await expand(item,availableSlots,token);
                    token.ThrowIfCancellationRequested();
                    items.RemoveAt(index);
                    for(int offset=0;offset<tracks.Count;offset++) items.Insert(index+offset,tracks[offset]);
                    index--;
                    queueChanged?.Invoke();
                    continue;
                }

                var folder=Destination(root,item.Category);
                item.Output=await download(item,folder,s=>
                {
                    if(s.State.StartsWith("Resolving — ",StringComparison.Ordinal))
                        item.Title=s.State.Substring("Resolving — ".Length);
                    if(s.State is "Downloading" or "Converting") item.State=s.State;
                    changed(item,s);
                },token);
                if(item.Output is null)
                {
                    item.State="Skipped";
                    changed(item,new("Skipped — already downloaded"));
                }
                else
                {
                    item.State="Complete";
                    changed(item,new("Complete",100,item.Output));
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested)
            {item.State="Cancelled";changed(item,new("Cancelled"));throw;}
            catch(Exception ex)
            {item.State="Failed";item.Error=ex.Message;changed(item,new("Failed — "+ex.Message));}
        }
    }
}
