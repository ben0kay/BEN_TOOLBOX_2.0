using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class YouTubeAudioDownloaderWindow : Window
{
    private readonly YouTubeAudioService service=new();
    private readonly TextBox url=new(){PlaceholderText="https://www.youtube.com/watch?v=…"};
    private readonly TextBox destination=new(){Text=Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)};
    private readonly TextBox executable=new(){Text=YouTubeAudioService.Find("yt-dlp")??""};
    private readonly TextBox ffmpeg=new(){Text=Path.GetDirectoryName(YouTubeAudioService.Find("ffmpeg")??"")??""};
    private readonly ComboBox quality=new(){ItemsSource=new[]{"Best (VBR 0)","High (VBR 2)","192 kbps","128 kbps"},SelectedIndex=0};
    private readonly CheckBox standalone=new(){Content=new TextBlock{Text="I confirm this is an official standalone release binary (enables -U)",TextWrapping=TextWrapping.Wrap}};
    private readonly TextBlock status=new(){Text="Select yt-dlp.exe under Tools, paste a link, then download.",TextWrapping=TextWrapping.Wrap};
    private readonly TextBox diagnostics=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=160};
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=8};
    private readonly Button cancel=new(){Content="CANCEL",IsEnabled=false};
    private readonly StackPanel inputs=new(){Spacing=12};
    private readonly ConcurrentQueue<string> messages=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private AudioStatus? latest;
    private CancellationTokenSource? active;
    private bool closing;
    public YouTubeAudioDownloaderWindow()
    {
        Title="YouTube Audio Downloader";Width=920;Height=850;MinWidth=650;MinHeight=580;
        Background=Brush.Parse("#0B1424");
        var root=new StackPanel{Margin=new Thickness(24),Spacing=16};
        root.Children.Add(new TextBlock{Text="YOUTUBE AUDIO DOWNLOADER",FontSize=25,Foreground=Brush.Parse("#52DCEC")});
        root.Children.Add(new TextBlock{Text="Single YouTube video → MP3 · local yt-dlp + FFmpeg",TextWrapping=TextWrapping.Wrap});
        inputs.Children.Add(Field("VIDEO URL",url));inputs.Children.Add(Field("SAVE TO",destination));
        inputs.Children.Add(ActionButton("BROWSE OUTPUT FOLDER",async()=>await Folder(destination)));
        inputs.Children.Add(Field("MP3 QUALITY",quality));
        inputs.Children.Add(new TextBlock{Text="A higher conversion bitrate does not restore detail missing from the source.",TextWrapping=TextWrapping.Wrap});
        inputs.Children.Add(ActionButton("DOWNLOAD MP3",()=>Run(async token=>
        {
            var settings=new YouTubeAudioOptions(executable.Text??"",ffmpeg.Text??"",destination.Text??"",new[]{"0","2","192K","128K"}[quality.SelectedIndex]);
            await service.DownloadAsync(settings,url.Text??"",value=>Interlocked.Exchange(ref latest,value),Log,token);
        })));
        var tools=new StackPanel{Spacing=10};
        tools.Children.Add(Field("EXACT yt-dlp EXECUTABLE",executable));
        tools.Children.Add(ActionButton("SELECT yt-dlp.exe",async()=>
        {
            var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Choose your trusted yt-dlp executable",AllowMultiple=false});
            if(files.Count>0){executable.Text=files[0].TryGetLocalPath();standalone.IsChecked=false;}
        }));
        tools.Children.Add(Field("FFMPEG / FFPROBE FOLDER (blank = auto-detect)",ffmpeg));
        tools.Children.Add(ActionButton("SELECT FFMPEG FOLDER",async()=>await Folder(ffmpeg)));
        tools.Children.Add(standalone);
        tools.Children.Add(new TextBlock{Text="Unknown / pip / winget installation? Update with its original package manager. This tool never installs binaries or elevates privileges.",TextWrapping=TextWrapping.Wrap});
        tools.Children.Add(ActionButton("CHECK FOR UPDATES",()=>Run(token=>service.UpdateAsync(executable.Text??"",standalone.IsChecked==true,Log,token))));
        tools.Children.Add(ActionButton("CHECK DEPENDENCIES / VERSION",()=>Run(async token=>
        {
            Log("Executable: "+executable.Text);Log("yt-dlp version: "+await service.VersionAsync(executable.Text??"",token));
            var adjacent=Path.GetDirectoryName(executable.Text??"");
            Log("FFmpeg: "+(YouTubeAudioService.Find("ffmpeg",ffmpeg.Text)??YouTubeAudioService.Find("ffmpeg",adjacent)??"MISSING"));
            Log("ffprobe: "+(YouTubeAudioService.Find("ffprobe",ffmpeg.Text)??YouTubeAudioService.Find("ffprobe",adjacent)??"MISSING"));
            Log("Deno: "+(YouTubeAudioService.Find("deno",adjacent)??"Not detected; YouTube extraction may need Deno/EJS."));
            Log("EJS guidance: https://github.com/yt-dlp/yt-dlp/wiki/EJS");
        })));
        inputs.Children.Add(new Expander{Header="TOOLS / TROUBLESHOOTING",IsExpanded=true,Content=tools});
        inputs.Children.Add(ActionButton("OPEN OUTPUT FOLDER",()=>
        {
            if(!Directory.Exists(destination.Text))throw new DirectoryNotFoundException("Choose an existing folder.");
            Process.Start(new ProcessStartInfo(Path.GetFullPath(destination.Text!)){UseShellExecute=true});return Task.CompletedTask;
        }));
        root.Children.Add(inputs);root.Children.Add(cancel);root.Children.Add(progress);root.Children.Add(status);
        root.Children.Add(new Expander{Header="DIAGNOSTICS",IsExpanded=true,Content=diagnostics});
        Content=new ScrollViewer{Content=root,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        Opened+=async(_,_)=>
        {
            inputs.IsEnabled=false;
            var saved=await YouTubeAudioSettingsStore.LoadAsync();
            if(saved is not null){executable.Text=saved.Executable;ffmpeg.Text=saved.FfmpegFolder;destination.Text=saved.OutputFolder;quality.SelectedIndex=Math.Clamp(saved.Quality,0,3);}
            inputs.IsEnabled=true;
        };
        cancel.Click+=(_,_)=>active?.Cancel();
        timer.Tick+=(_,_)=>Flush();
        Closing+=(_,e)=>{if(active is not null){e.Cancel=true;closing=true;active.Cancel();}};
        Closed+=(_,_)=>timer.Stop();
    }
    private static Control Field(string label,Control control)
    { var panel=new StackPanel{Spacing=5};panel.Children.Add(new TextBlock{Text=label,FontSize=12});panel.Children.Add(control);return panel; }
    private Button ActionButton(string title,Func<Task> action)
    {
        var button=new Button{Content=title};button.Click+=async(_,_)=>{try{await action();}catch(Exception ex){status.Text=ex.Message;}};return button;
    }
    private async Task Folder(TextBox box)
    {var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{AllowMultiple=false});if(folders.Count>0)box.Text=folders[0].TryGetLocalPath();}
    private void Log(string message){messages.Enqueue(message);while(messages.Count>300)messages.TryDequeue(out _);}
    private void Flush()
    {
        while(messages.TryDequeue(out var message))diagnostics.Text+=(message+Environment.NewLine);
        if((diagnostics.Text?.Length??0)>24000)diagnostics.Text=diagnostics.Text![^24000..];
        var value=Interlocked.Exchange(ref latest,null);
        if(value is not null){status.Text=value.State+(value.Output is null?"":" — "+value.Output);progress.IsIndeterminate=value.Percent is null;progress.Value=value.Percent??0;}
    }
    private async Task Run(Func<CancellationToken,Task> operation)
    {
        if(active is not null)return;
        active=new CancellationTokenSource();inputs.IsEnabled=false;cancel.IsEnabled=true;progress.IsIndeterminate=true;status.Text="Working…";timer.Start();
        try
        {
            try{await YouTubeAudioSettingsStore.SaveAsync(new(executable.Text??"",ffmpeg.Text??"",destination.Text??"",quality.SelectedIndex));}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Log("Could not remember preferences: "+ex.Message);}
            await operation(active.Token);Flush();if(status.Text=="Working…")status.Text="Finished. See diagnostics.";}
        catch(OperationCanceledException){Flush();status.Text="Cancelled. Any intermediate files are listed in diagnostics.";}
        catch(Exception ex){Flush();status.Text=ex.Message;Log("Check diagnostics for network/extractor, FFmpeg or Deno/EJS errors.");Flush();}
        finally{active.Dispose();active=null;timer.Stop();inputs.IsEnabled=true;cancel.IsEnabled=false;progress.IsIndeterminate=false;if(closing)Close();}
    }
}
