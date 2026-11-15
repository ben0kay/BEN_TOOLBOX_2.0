using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record YouTubeAudioOptions(string Executable, string FfmpegFolder, string OutputFolder, string Quality = "0");
public sealed record AudioStatus(string State, double? Percent = null, string? Output = null);

public sealed class YouTubeAudioService
{
    public static string NormalizePath(string value) => value.Trim().Trim('"');
    private readonly SemaphoreSlim gate = new(1,1);
    public static string? Find(string name, string? adjacent = null)
    {
        var filename = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        foreach (var dir in new[]{adjacent}.Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)))
            if (!string.IsNullOrWhiteSpace(dir)) { var path=Path.Combine(dir.Trim('"'),filename); if(File.Exists(path)) return Path.GetFullPath(path); }
        return null;
    }
    public static string ValidateUrl(string value) => ClassifyUrl(value).Url;

    internal static (string Url, bool IsPlaylist) ClassifyUrl(string value, bool allowSoundCloudTrackId = false)
    {
        if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri) || uri.Scheme!="https" ||
           !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw new ArgumentException("Enter an HTTPS YouTube or SoundCloud track or playlist link.");

        var host=uri.IdnHost;
        var segments=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(host=="youtu.be" && segments.Length==1 && IsSlug(segments[0]))
            return (uri.AbsoluteUri,false);

        if(host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            var query=uri.Query.TrimStart('?').Split('&');
            if(uri.AbsolutePath=="/playlist" && query.Any(x=>x.StartsWith("list=",StringComparison.Ordinal) && IsSlug(x[5..])))
                return (uri.AbsoluteUri,true);
            if(uri.AbsolutePath=="/watch" && query.Any(x=>x.StartsWith("v=",StringComparison.Ordinal) && x.Length>2) ||
               segments.Length==2 && (segments[0] is "shorts" or "live") && IsSlug(segments[1]))
                return (uri.AbsoluteUri,false);
        }

        if((host is "soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com") &&
           segments.Length>=2 && segments.All(IsSlug) &&
           !new[]{"stations","discover","search","stream","charts","tags","pages","you"}.Contains(segments[0],StringComparer.OrdinalIgnoreCase))
        {
            if(segments.Length==3 && segments[1]=="sets")
                return (uri.AbsoluteUri,true);
            // Preserve individual-track links with their optional share token.
            if((segments.Length==2 || segments.Length==3 && segments[2].StartsWith("s-",StringComparison.OrdinalIgnoreCase)) &&
               !new[]{"tracks","albums","sets","reposts","likes","spotlight","comments","recommended"}.Contains(segments[1],StringComparer.OrdinalIgnoreCase))
                return (uri.AbsoluteUri,false);
        }
        if(host=="on.soundcloud.com" && segments.Length==1 && IsSlug(segments[0]))
            return (uri.AbsoluteUri,false);

        // SoundCloud sets sometimes provide only a public API track ID.
        if(allowSoundCloudTrackId && (host is "api.soundcloud.com" or "api-v2.soundcloud.com") &&
           segments.Length==2 && segments[0]=="tracks" && segments[1].All(char.IsAsciiDigit) && uri.Query.Length==0)
            return (uri.AbsoluteUri,false);

        throw new ArgumentException("Use a YouTube video or playlist, or a SoundCloud track or set link. Profiles and feeds are unsupported.");
    }

    private static bool IsSlug(string value) => value.Length>0 && value.All(c=>char.IsLetterOrDigit(c) || c=='-' || c=='_');

    private static string ValidateTrackUrl(string value)
    {
        var media=ClassifyUrl(value,allowSoundCloudTrackId:true);
        if(media.IsPlaylist) throw new ArgumentException("Expand the playlist before downloading its tracks.");
        return media.Url;
    }

    internal async Task<IReadOnlyList<AudioQueueItem>> ExpandPlaylistAsync(string exe,AudioQueueItem playlist,
        int availableSlots,Action<string> log,CancellationToken token)
    {
        if(!playlist.IsPlaylist) throw new ArgumentException("Expected a playlist link.");
        if(availableSlots<1 || availableSlots>YouTubeAudioQueue.MaximumItems)
            throw new ArgumentOutOfRangeException(nameof(availableSlots));
        if(!await gate.WaitAsync(0,token)) throw new InvalidOperationException("An operation is already running.");
        try
        {
            var tracks=new List<AudioQueueItem>();
            var host=new Uri(playlist.Url).IdnHost;
            var youtube=host=="youtube.com" || host.EndsWith(".youtube.com",StringComparison.Ordinal);
            int seen=0;
            // Read one extra entry to detect overflow without retaining or enumerating the full set.
            await RunAsync(exe,new[]{"--ignore-config","--yes-playlist","--flat-playlist","--lazy-playlist","--simulate",
                "--playlist-items","1:"+(availableSlots+1).ToString(CultureInfo.InvariantCulture),
                "--print","%(.{id,url,webpage_url,title,availability})j","--",playlist.Url},line=>
            {
                if(++seen>availableSlots) return;
                using var json=JsonDocument.Parse(line);
                tracks.Add(ReadPlaylistTrack(json.RootElement,playlist,youtube));
            },token,line=>log(Redact(line))).ConfigureAwait(false);
            if(seen>availableSlots)
                throw new IOException($"This playlist exceeds the queue limit of {YouTubeAudioQueue.MaximumItems} tracks. Only {availableSlots} slots are available.");
            if(tracks.Count==0)
                throw new IOException("Playlist is empty or unavailable.");
            return tracks;
        }
        finally { gate.Release(); }
    }

    private static AudioQueueItem ReadPlaylistTrack(JsonElement entry,AudioQueueItem playlist,bool youtube)
    {
        static string? Field(JsonElement entry,string name) =>
            entry.ValueKind==JsonValueKind.Object && entry.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String
                ? value.GetString() : null;

        var title=Field(entry,"title");
        if(Field(entry,"availability") is not ("private" or "needs_auth" or "premium_only" or "subscriber_only"))
        {
            var id=Field(entry,"id");
            var candidates=youtube && id is not null && IsSlug(id)
                ? new[]{"https://www.youtube.com/watch?v="+id}
                : new[]{Field(entry,"url"),Field(entry,"webpage_url")};
            foreach(var candidate in candidates)
            {
                if(candidate is null || !Uri.TryCreate(candidate,UriKind.Absolute,out var uri)) continue;
                var host=uri.IdnHost;
                if(youtube ? host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be")
                           : host is not ("soundcloud.com" or "www.soundcloud.com" or "m.soundcloud.com" or "api.soundcloud.com" or "api-v2.soundcloud.com"))
                    continue;
                try { return AudioQueueItem.PlaylistTrack(ValidateTrackUrl(candidate),playlist.Category,title); }
                catch(ArgumentException) { }
            }
        }
        return AudioQueueItem.UnavailableTrack(playlist,title);
    }
    public static List<string> DownloadArguments(string url, string stage, string ffmpeg, string quality, string? output = null)
    {
        if(!new[]{"0","2","192K","128K"}.Contains(quality)) throw new ArgumentException("Unsupported MP3 quality.");
        // The final home folder must be visible to yt-dlp for its native existing-MP3 check.
        // Keep in-progress media in a unique per-job temp folder; track completed IDs by site.
        output ??= stage;
        var archive=Path.Combine(output,".btx-audio-download-archive.txt");
        return new(){"--ignore-config","--no-quiet","--no-playlist","--no-overwrites","--no-post-overwrites",
            "--download-archive",archive,"-f","bestaudio/best","-x","--audio-format","mp3","--audio-quality",quality,
            "--newline","--progress","--progress-template","download:BTX_PROGRESS:%(progress._percent_str)s",
            "--print","after_move:BTX_FILE:%(filepath)j","--print","before_dl:BTX_TITLE:%(title)s",
            "--no-simulate","--windows-filenames","--ffmpeg-location",ffmpeg,
            "--paths","home:"+output,"--paths","temp:"+stage,
            "-o","%(title).150B [%(id)s].%(ext)s","--",ValidateTrackUrl(url)};
    }
    // Null means yt-dlp itself skipped a completed media ID or an MP3 already in the destination.
    public async Task<string?> DownloadAsync(YouTubeAudioOptions options,string url,Action<AudioStatus> status,Action<string> log,CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token)) throw new InvalidOperationException("An operation is already running.");
        string? stage=null;
        try
        {
            options=options with { Executable=NormalizePath(options.Executable), FfmpegFolder=NormalizePath(options.FfmpegFolder), OutputFolder=NormalizePath(options.OutputFolder) };
            ValidateTrackUrl(url);
            var output=Path.GetFullPath(options.OutputFolder);
            if(!Directory.Exists(output)) throw new DirectoryNotFoundException("Choose an existing output folder.");
            var adjacent=Path.GetDirectoryName(Path.GetFullPath(options.Executable));
            var ffmpeg=Find("ffmpeg",options.FfmpegFolder)??Find("ffmpeg",adjacent);
            var probe=Find("ffprobe",ffmpeg is null?null:Path.GetDirectoryName(ffmpeg));
            if(ffmpeg is null||probe is null) throw new FileNotFoundException("FFmpeg and ffprobe are required. Select their bin folder in Tools.");
            // yt-dlp sees the real destination for native duplicate detection, while temporary
            // downloads stay isolated so failed/cancelled jobs cannot overwrite existing MP3s.
            stage=Path.Combine(output,".youtube-audio-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string? reported=null;bool skipped=false;status(new("Resolving"));
            await RunAsync(options.Executable,DownloadArguments(url,stage,Path.GetDirectoryName(ffmpeg)!,options.Quality,output),line=>
            {
                if(line.StartsWith("BTX_PROGRESS:"))
                {
                    var raw=line.Substring(13).Trim().TrimEnd('%');
                    status(new("Downloading",double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?Math.Clamp(n,0,100):null));
                }
                else if(line.StartsWith("BTX_FILE:")) reported=JsonSerializer.Deserialize<string>(line.Substring(9));
                else if(line.StartsWith("BTX_TITLE:")) { log("Title: "+line.Substring(10)); status(new("Resolving — "+line.Substring(10))); }
                else
                {
                    if(line.StartsWith("[download]",StringComparison.Ordinal) &&
                        (line.Contains("has already been recorded in the archive",StringComparison.Ordinal) ||
                         line.Contains("has already been downloaded",StringComparison.Ordinal)))
                        skipped=true;
                    log(Redact(line));
                    if(line.Contains("[ExtractAudio]")) status(new("Converting"));
                }
            },token);
            token.ThrowIfCancellationRequested();
            if(skipped)
            {
                log("Skipped: yt-dlp found this media in its archive or an MP3 already at the destination.");
                return null;
            }
            if(reported is null) throw new IOException("yt-dlp did not report a completed output file.");
            var final=Path.GetFullPath(reported);
            if(!string.Equals(Path.GetDirectoryName(final),output,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(final),".mp3",StringComparison.OrdinalIgnoreCase) || !File.Exists(final) || new FileInfo(final).Length==0)
                throw new IOException("yt-dlp output was missing or outside the selected destination.");
            status(new("Complete",100,final));
            return final;
        }
        finally
        {
            try
            {
                if(stage is not null && Directory.Exists(stage))
                {
                    if(!Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
                    else log("Intermediate files retained in: "+stage);
                }
            }
            finally { gate.Release(); }
        }
    }
    public async Task<string> VersionAsync(string exe,CancellationToken token)
    {
        string version=""; using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await RunAsync(exe,new[]{"--ignore-config","--version"},line=>version=line,timeout.Token); return version;
    }
    public async Task UpdateAsync(string exe,bool confirmedStandalone,Action<string> log,CancellationToken token)
    {
        exe=NormalizePath(exe);
        if(!confirmedStandalone) throw new InvalidOperationException("For pip, winget or another package manager, update through that manager. Confirm standalone release binary only if known.");
        if(!await gate.WaitAsync(0,token)) throw new InvalidOperationException("An operation is already running.");
        try
        {
            log("Selected executable: "+Path.GetFullPath(exe)); log("Before: "+await VersionAsync(exe,token));
            await RunAsync(exe,new[]{"--ignore-config","-U"},line=>log(Redact(line)),token);
            log("After: "+await VersionAsync(exe,token)); log("Update command succeeded. Try your URL again.");
        }
        finally { gate.Release(); }
    }
    public static string Redact(string line) => System.Text.RegularExpressions.Regex.Replace(line,@"https?://[^\s]+","[URL redacted]");
    public static async Task RunAsync(string exe,IEnumerable<string> arguments,Action<string> line,CancellationToken token,Action<string>? errorLine=null)
    {
        token.ThrowIfCancellationRequested();
        exe=NormalizePath(exe);
        if(!Path.IsPathFullyQualified(exe)||!File.Exists(exe)) throw new FileNotFoundException("Select the exact yt-dlp executable first.");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments) start.ArgumentList.Add(argument);
        using var process=new Process{StartInfo=start}; process.Start();
        using var cancel=token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} catch(System.ComponentModel.Win32Exception){} });
        Exception? callbackError=null;
        async Task Drain(StreamReader reader,Action<string> receive)
        {
            while(await reader.ReadLineAsync().ConfigureAwait(false) is {} value)
            {
                try { receive(value.Length>4000?value.Substring(0,4000):value); }
                catch(Exception ex) { Interlocked.CompareExchange(ref callbackError,ex,null); }
            }
        }
        var drains=Task.WhenAll(Drain(process.StandardOutput,line),Drain(process.StandardError,errorLine??line));
        try { await process.WaitForExitAsync(); await drains; token.ThrowIfCancellationRequested(); if(callbackError is not null) throw new IOException("Could not parse tool output.",callbackError); if(process.ExitCode!=0) throw new IOException($"Tool exited with code {process.ExitCode}. See diagnostics for the cause."); }
        finally { if(!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
