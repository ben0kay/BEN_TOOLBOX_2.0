using BEN_TOOLBOX_2_0.Services;
using System.Text.Json;

void Check(bool condition,string message){if(!condition)throw new Exception(message);}
async Task Fails<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
Check(YouTubeAudioService.NormalizePath(" \"D:\\folder name\\yt-dlp.exe\" ")=="D:\\folder name\\yt-dlp.exe","Quoted paths");
foreach(var valid in new[]{"https://youtu.be/abc","https://www.youtube.com/watch?v=abc&list=xyz",
    "https://soundcloud.com/example-artist/example-track","https://www.soundcloud.com/artist_1/song-2026?si=share",
    "https://m.soundcloud.com/artist/track","https://soundcloud.com/artist/track/s-privatecode",
    "https://on.soundcloud.com/AbC123"})
    Check(YouTubeAudioService.ValidateUrl(valid)==new Uri(valid).AbsoluteUri,"Valid media URL rejected: "+valid);
foreach(var invalid in new[]{"http://youtube.com/watch?v=a","https://youtube.com.evil.test/watch?v=a","https://youtube.com/playlist?list=x","--exec=oops","https://user@youtube.com/watch?v=a",
    "http://soundcloud.com/artist/track","https://soundcloud.com.evil.test/artist/track","https://user@soundcloud.com/artist/track",
    "https://soundcloud.com/artist","https://soundcloud.com/artist/sets/playlist","https://soundcloud.com/artist/tracks",
    "https://soundcloud.com/artist/likes","https://soundcloud.com/stations/track","https://soundcloud.com/artist/track/albums",
    "https://on.soundcloud.com/","https://not-on.soundcloud.com/abc"})
    await Fails<ArgumentException>(()=>Task.FromResult(YouTubeAudioService.ValidateUrl(invalid)));
var argsList=YouTubeAudioService.DownloadArguments("https://www.youtube.com/watch?v=abc&list=xyz","/stage path","/ffmpeg","0","/output path");
Check(argsList[^1].Contains("&list=xyz")&&argsList[^2]=="--"&&argsList.Contains("--ignore-config")&&argsList.Contains("--no-playlist") &&
    argsList.Contains("--download-archive")&&argsList.Contains("--no-post-overwrites")&&
    argsList.Contains("home:/output path")&&argsList.Contains("temp:/stage path"),"Arguments/paths unsafe");
var soundCloudArgs=YouTubeAudioService.DownloadArguments("https://soundcloud.com/artist/track?si=code","/stage path","/ffmpeg","0","/output path");
Check(soundCloudArgs[^1].Contains("soundcloud.com/artist/track")&&soundCloudArgs[^2]=="--"&&soundCloudArgs.Contains("--no-playlist")&&soundCloudArgs.Contains("mp3"),"SoundCloud argument handling unsafe");
Check(YouTubeAudioService.Redact("error https://youtube.com/watch?v=secret")=="error [URL redacted]","Redaction");
if(OperatingSystem.IsWindows()){Console.WriteLine("PASS URL/argv checks; fake executable fixture currently requires Linux/python3.");return;}
var root=Path.Combine(Path.GetTempPath(),"yt smoke "+Guid.NewGuid());Directory.CreateDirectory(root);
try
{
    var exe=Path.Combine(root,"fake yt-dlp");var capture=Path.Combine(root,"argv.json");
    var script="""
#!/usr/bin/env python3
import sys,os,json,time
args=sys.argv[1:]
base=os.path.dirname(__file__)
open(os.path.join(base,'argv.json'),'w').write(json.dumps(args))
if '--version' in args: print('2026.test');sys.exit(0)
if '-U' in args: print('Already up to date');sys.exit(0)
url=args[-1]
if 'fail' in url: print('extractor failure',file=sys.stderr);sys.exit(2)
if 'slow' in url: time.sleep(30)
if 'badoutput' in url: print('BTX_FILE:'+json.dumps('/tmp/outside.mp3'));sys.exit(0)
home=next(p[5:] for p in args if p.startswith('home:'))
stage=next(p[5:] for p in args if p.startswith('temp:'))
archive=args[args.index('--download-archive')+1]
identity='soundcloud 123' if 'soundcloud.com' in url else 'youtube abc'
media_id='sc' if 'soundcloud.com' in url else 'yt'
if os.path.isfile(archive) and identity in open(archive).read().splitlines():
    print('[download] '+identity+': has already been recorded in the archive')
    sys.exit(0)
file=os.path.join(home,'Song ['+media_id+'].mp3')
if os.path.isfile(file):
    print('[download] '+file+' has already been downloaded')
else:
    with open(file,'xb') as output: output.write(b'fake MP3 fixture')
    print('BTX_PROGRESS: 55.0%')
    print('[ExtractAudio] Converting')
with open(archive,'a') as output: output.write(identity+'\n')
print('BTX_FILE:'+json.dumps(file))
""";
    File.WriteAllText(exe,script);File.SetUnixFileMode(exe,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    File.WriteAllText(Path.Combine(root,"ffmpeg"),"");File.WriteAllText(Path.Combine(root,"ffprobe"),"");
    var service=new YouTubeAudioService();var options=new YouTubeAudioOptions(exe,root,root);var log=new List<string>();var states=new List<AudioStatus>();
    // A pre-existing MP3 is skipped even before its media ID has been archived.
    File.WriteAllText(Path.Combine(root,"Song [sc].mp3"),"existing");
    var soundCloudResult=await service.DownloadAsync(options,"https://soundcloud.com/artist/track",s=>states.Add(s),s=>log.Add(s),default);
    Check(soundCloudResult is null&&File.ReadAllText(Path.Combine(root,"Song [sc].mp3"))=="existing","Pre-existing SoundCloud file was not skipped");
    var soundCloudAgain=await service.DownloadAsync(options,"https://soundcloud.com/artist/track",s=>states.Add(s),s=>log.Add(s),default);
    Check(soundCloudAgain is null,"SoundCloud archive duplicate was not skipped");
    var result=await service.DownloadAsync(options,"https://youtu.be/abc",s=>states.Add(s),s=>log.Add(s),default);
    Check(result is not null&&result.EndsWith("Song [yt].mp3")&&File.Exists(result),"YouTube MP3 output missing");
    Check(states.Any(s=>s.Percent==55)&&states.Last().State=="Complete","Progress/completion");
    Check(File.ReadAllLines(Path.Combine(root,".btx-audio-download-archive.txt")).Length==2,"Archive did not record both providers");
    // Archive skips even if the old output was moved away, because yt-dlp uses media IDs.
    File.Delete(result!);
    var archivedAgain=await service.DownloadAsync(options,"https://youtu.be/abc",s=>states.Add(s),s=>log.Add(s),default);
    Check(archivedAgain is null&&!File.Exists(result),"Previously archived media was downloaded twice");
    await Fails<IOException>(()=>service.DownloadAsync(options,"https://youtu.be/badoutput",_=>{},_=>{},default));
    await Fails<IOException>(()=>service.DownloadAsync(options,"https://youtu.be/fail",_=>{},_=>{},default));
    using(var cancel=new CancellationTokenSource(250))await Fails<OperationCanceledException>(()=>service.DownloadAsync(options,"https://youtu.be/slow",_=>{},_=>{},cancel.Token));
    using(var stop=new CancellationTokenSource())
    {
        var slow=service.DownloadAsync(options,"https://youtu.be/slow",_=>{},_=>{},stop.Token);
        await Fails<InvalidOperationException>(()=>service.UpdateAsync(exe,true,_=>{},default));
        stop.Cancel();await Fails<OperationCanceledException>(()=>slow);
    }
    var oldPath=Environment.GetEnvironmentVariable("PATH");
    try
    {
        Environment.SetEnvironmentVariable("PATH","");
        File.Delete(Path.Combine(root,"ffprobe"));
        await Fails<FileNotFoundException>(()=>service.DownloadAsync(options,"https://youtu.be/abc",_=>{},_=>{},default));
    }
    finally{Environment.SetEnvironmentVariable("PATH",oldPath);}
    await Fails<InvalidOperationException>(()=>service.UpdateAsync(exe,false,_=>{},default));
    await service.UpdateAsync("\""+exe+"\"",true,s=>log.Add(s),default);
    Check(log.Any(s=>s.StartsWith("Before:"))&&log.Any(s=>s.StartsWith("After:")),"Update versions missing");
    await Fails<FileNotFoundException>(()=>service.VersionAsync(Path.Combine(root,"missing"),default));
    var queue=YouTubeAudioQueue.Parse("https://youtu.be/one\nhttps://youtu.be/fail\nhttps://soundcloud.com/artist/three",AudioCategory.Music);
    queue[1].Category=AudioCategory.Other;
    var destinations=new List<string>();int calls=0;
    await YouTubeAudioQueue.RunAsync(queue,root,(item,folder,changed,ct)=>
    {
        calls++;destinations.Add(folder);
        if(item.Url.EndsWith("fail"))throw new IOException("fixture failure");
        return Task.FromResult<string?>(item.Url.Contains("soundcloud.com")?null:Path.Combine(folder,"song.mp3"));
    },(_,_)=>{},default);
    Check(queue[0].State=="Complete"&&queue[1].State=="Failed"&&queue[2].State=="Skipped","Queue did not mark SoundCloud duplicate as skipped");
    Check(destinations[0]==Path.Combine(root,"Music")&&destinations[1]==Path.Combine(root,"Other"),"Per-link routing");
    await YouTubeAudioQueue.RunAsync(queue,root,(_,_,_,_)=>{calls++;return Task.FromResult("retry.mp3");},(_,_)=>{},default);
    Check(calls==4,"Queue retried completed/skipped items");
    using(var stopQueue=new CancellationTokenSource())
    {
        var waiting=YouTubeAudioQueue.Parse("https://youtu.be/one\nhttps://youtu.be/two",AudioCategory.Other);
        await Fails<OperationCanceledException>(()=>YouTubeAudioQueue.RunAsync(waiting,root,(_,_,_,ct)=>{stopQueue.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult("");},(_,_)=>{},stopQueue.Token));
        Check(waiting[0].State=="Cancelled"&&waiting[1].State=="Waiting","Queue cancellation continued into remaining items");
    }
    await Fails<ArgumentException>(()=>Task.FromResult(YouTubeAudioQueue.Parse("https://youtu.be/one\nbad link",AudioCategory.Other)));
    Console.WriteLine("PASS: per-category queue, native duplicate skip, continue on failure, skip complete/skipped on retry and cancel remainder.");
    Console.WriteLine("PASS: YouTube/SoundCloud URL, archive/argv safety, existing file skip, progress, staged output, failure, cancellation and updater guard.");
}
finally{Directory.Delete(root,true);}
