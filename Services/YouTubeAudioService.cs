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
    public static string ValidateUrl(string value)
    {
        if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri) || uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw new ArgumentException("Enter an HTTPS YouTube video or SoundCloud track URL.");
        var host=uri.IdnHost.ToLowerInvariant();
        bool youtube=host=="youtu.be" ? uri.AbsolutePath.Trim('/').Length>0 :
            new[]{"youtube.com","www.youtube.com","m.youtube.com","music.youtube.com"}.Contains(host) &&
            (uri.AbsolutePath=="/watch" && uri.Query.Split('&').Any(x=>x.TrimStart('?').StartsWith("v=",StringComparison.Ordinal) && x.Length>2) ||
             uri.AbsolutePath.StartsWith("/shorts/",StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/live/",StringComparison.Ordinal));

        bool soundcloud=false;
        if(new[]{"soundcloud.com","www.soundcloud.com","m.soundcloud.com"}.Contains(host))
        {
            // Only individual tracks, including optional private-track tokens; never a user, sets or feed page.
            var segments=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
            soundcloud=(segments.Length==2 || segments.Length==3 && segments[2].StartsWith("s-",StringComparison.OrdinalIgnoreCase)) &&
                segments.All(segment=>segment.Length>0 && segment.All(c=>char.IsLetterOrDigit(c) || c=='-' || c=='_')) &&
                !new[]{"tracks","albums","sets","reposts","likes","spotlight","comments","recommended"}.Contains(segments[1],StringComparer.OrdinalIgnoreCase) &&
                !new[]{"stations","discover","search","stream","charts","tags","pages","you"}.Contains(segments[0],StringComparer.OrdinalIgnoreCase);
        }
        else if(host=="on.soundcloud.com")
        {
            // SoundCloud share links redirect to a track; yt-dlp handles the redirect when available.
            var segments=uri.AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);
            soundcloud=segments.Length==1 && segments[0].All(c=>char.IsLetterOrDigit(c) || c=='-' || c=='_');
        }
        if(!youtube && !soundcloud) throw new ArgumentException("Use a single YouTube video or SoundCloud track link, not a channel, profile or playlist.");
        return uri.AbsoluteUri;
    }
    public static List<string> DownloadArguments(string url, string stage, string ffmpeg, string quality, string? output = null)
    {
        if(!new[]{"0","2","192K","128K"}.Contains(quality)) throw new ArgumentException("Unsupported MP3 quality.");
        // The final home folder must be visible to yt-dlp for its native existing-MP3 check.
        // Keep in-progress media in a unique per-job temp folder; track completed IDs by site.
        output ??= stage;
        var archive=Path.Combine(output,".btx-audio-download-archive.txt");
        return new(){"--ignore-config","--no-playlist","--no-overwrites","--no-post-overwrites",
            "--download-archive",archive,"-f","bestaudio/best","-x","--audio-format","mp3","--audio-quality",quality,
            "--newline","--progress","--progress-template","download:BTX_PROGRESS:%(progress._percent_str)s",
            "--print","after_move:BTX_FILE:%(filepath)j","--print","before_dl:BTX_TITLE:%(title)s",
            "--no-simulate","--windows-filenames","--ffmpeg-location",ffmpeg,
            "--paths","home:"+output,"--paths","temp:"+stage,
            "-o","%(title).150B [%(id)s].%(ext)s","--",ValidateUrl(url)};
    }
    // Null means yt-dlp itself skipped a completed media ID or an MP3 already in the destination.
    public async Task<string?> DownloadAsync(YouTubeAudioOptions options,string url,Action<AudioStatus> status,Action<string> log,CancellationToken token)
    {
        if(!await gate.WaitAsync(0,token)) throw new InvalidOperationException("An operation is already running.");
        string? stage=null;
        try
        {
            options=options with { Executable=NormalizePath(options.Executable), FfmpegFolder=NormalizePath(options.FfmpegFolder), OutputFolder=NormalizePath(options.OutputFolder) };
            ValidateUrl(url);
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
    public static async Task RunAsync(string exe,IEnumerable<string> arguments,Action<string> line,CancellationToken token)
    {
        exe=NormalizePath(exe);
        if(!Path.IsPathFullyQualified(exe)||!File.Exists(exe)) throw new FileNotFoundException("Select the exact yt-dlp executable first.");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in arguments) start.ArgumentList.Add(argument);
        using var process=new Process{StartInfo=start}; process.Start();
        using var cancel=token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} catch(System.ComponentModel.Win32Exception){} });
        Exception? callbackError=null;
        async Task Drain(StreamReader reader)
        {
            while(await reader.ReadLineAsync() is {} value)
            {
                try { line(value.Length>4000?value.Substring(0,4000):value); }
                catch(Exception ex) { Interlocked.CompareExchange(ref callbackError,ex,null); }
            }
        }
        var drains=Task.WhenAll(Drain(process.StandardOutput),Drain(process.StandardError));
        try { await process.WaitForExitAsync(); await drains; token.ThrowIfCancellationRequested(); if(callbackError is not null) throw new IOException("Could not parse tool output.",callbackError); if(process.ExitCode!=0) throw new IOException($"Tool exited with code {process.ExitCode}. See diagnostics for the cause."); }
        finally { if(!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
