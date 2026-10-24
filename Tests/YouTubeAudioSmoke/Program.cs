using BEN_TOOLBOX_2_0.Services;
using System.Text.Json;

void Check(bool condition,string message){if(!condition)throw new Exception(message);}
async Task Fails<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
foreach(var invalid in new[]{"http://youtube.com/watch?v=a","https://youtube.com.evil.test/watch?v=a","https://youtube.com/playlist?list=x","--exec=oops","https://user@youtube.com/watch?v=a"})
    await Fails<ArgumentException>(()=>Task.FromResult(YouTubeAudioService.ValidateUrl(invalid)));
var argsList=YouTubeAudioService.DownloadArguments("https://www.youtube.com/watch?v=abc&list=xyz","/space path","/ffmpeg","0");
Check(argsList[^1].Contains("&list=xyz")&&argsList[^2]=="--"&&argsList.Contains("--ignore-config")&&argsList.Contains("--no-playlist"),"Arguments unsafe");
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
stage=args[args.index('--paths')+1]
file=os.path.join(stage,'Song [abc].mp3')
open(file,'wb').write(b'fake MP3 fixture')
print('BTX_PROGRESS: 55.0%')
print('[ExtractAudio] Converting')
print('BTX_FILE:'+json.dumps(file))
""";
    File.WriteAllText(exe,script);File.SetUnixFileMode(exe,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    File.WriteAllText(Path.Combine(root,"ffmpeg"),"");File.WriteAllText(Path.Combine(root,"ffprobe"),"");
    var service=new YouTubeAudioService();var options=new YouTubeAudioOptions(exe,root,root);var log=new List<string>();var states=new List<AudioStatus>();
    File.WriteAllText(Path.Combine(root,"Song [abc].mp3"),"existing");
    var result=await service.DownloadAsync(options,"https://youtu.be/abc",s=>states.Add(s),s=>log.Add(s),default);
    Check(result.EndsWith("(1).mp3")&&File.ReadAllText(Path.Combine(root,"Song [abc].mp3"))=="existing","Existing output overwritten");
    Check(states.Any(s=>s.Percent==55)&&states.Last().State=="Complete","Progress/completion");
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
    await service.UpdateAsync(exe,true,s=>log.Add(s),default);
    Check(log.Any(s=>s.StartsWith("Before:"))&&log.Any(s=>s.StartsWith("After:")),"Update versions missing");
    await Fails<FileNotFoundException>(()=>service.VersionAsync(Path.Combine(root,"missing"),default));
    Console.WriteLine("PASS: URL/argv safety, progress, staged output/collisions, failure, cancellation, updater guard and version refresh, missing executable.");
}
finally{Directory.Delete(root,true);}
