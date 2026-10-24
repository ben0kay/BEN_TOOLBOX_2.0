using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class YouTubeAudioDownloaderWindow : Window
{
    private static readonly IBrush Cyan=Brush.Parse("#35D9FA"), Muted=Brush.Parse("#8AAFC4");
    private readonly YouTubeAudioService service=new();
    private readonly TextBox url=new(){PlaceholderText="Paste video links here — one URL per line",AcceptsReturn=true,MinHeight=76,MaxHeight=140,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox executable=new(){Text=OperatingSystem.IsWindows()?@"D:\Ben Data\Music\MANUAL DOWNLOADS\YTDLP\yt-dlp.exe":YouTubeAudioService.Find("yt-dlp")??""};
    private readonly TextBox destination=new(){Text=OperatingSystem.IsWindows()?@"D:\Ben Data\Music\MANUAL DOWNLOADS\YTDLP":Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)};
    private readonly TextBox ffmpeg=new(){Text=Path.GetDirectoryName(YouTubeAudioService.Find("ffmpeg")??"")??""};
    private readonly ComboBox quality=new(){ItemsSource=new[]{"Best (VBR 0)","High (VBR 2)","192 kbps","128 kbps"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly RadioButton music=new(){Content="MUSIC",GroupName="audio-category",IsChecked=true};
    private readonly RadioButton other=new(){Content="OTHER",GroupName="audio-category"};
    private readonly CheckBox standalone=new(){Content=new TextBlock{Text="Official standalone release binary — enable manual -U update",TextWrapping=TextWrapping.Wrap}};
    private readonly TextBlock status=new(){Text="READY · Add links to begin",TextWrapping=TextWrapping.Wrap,Foreground=Cyan,FontSize=18};
    private readonly TextBlock percent=new(){Text="—",Foreground=Cyan,FontSize=30,FontWeight=FontWeight.Bold};
    private readonly TextBlock summary=new(){Text="0 ITEMS · SEQUENTIAL DOWNLOADS",Foreground=Muted,FontSize=12};
    private readonly TextBlock current=new(){Text="Your queue is ready. Completed files will appear in Music or Other.",Foreground=Muted,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox diagnostics=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=125,FontFamily=new FontFamily("Consolas"),FontSize=11};
    private readonly ProgressBar progress=new(){Minimum=0,Maximum=100,Height=12,Foreground=Cyan,Background=Brush.Parse("#112A3C")};
    private readonly Button cancel=new(){Content="■  CANCEL QUEUE",IsEnabled=false};
    private readonly StackPanel editor=new(){Spacing=12}, tools=new(){Spacing=12}, rows=new(){Spacing=6};
    private readonly List<AudioQueueItem> queue=new();
    private readonly Dictionary<AudioQueueItem,TextBlock> rowStates=new();
    private readonly ConcurrentQueue<string> messages=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private sealed record UiProgress(AudioQueueItem Item, AudioStatus Status);
    private UiProgress? latest;
    private AudioQueueItem? selectedItem;
    private CancellationTokenSource? active;
    private bool closing;
    public YouTubeAudioDownloaderWindow()
    {
        Title="BEN TOOLBOX · Audio Downloader";Width=1220;Height=940;MinWidth=700;MinHeight=650;
        Background=Brush.Parse("#060F1C");
        Styles.Add(new Style(x=>x.OfType<TextBox>()){Setters={new Setter(TextBox.BackgroundProperty,Brush.Parse("#061420")),new Setter(TextBox.BorderBrushProperty,Brush.Parse("#24627C")),new Setter(TextBox.CornerRadiusProperty,new CornerRadius(3))}});
        Styles.Add(new Style(x=>x.OfType<ComboBox>()){Setters={new Setter(ComboBox.BackgroundProperty,Brush.Parse("#061420")),new Setter(ComboBox.BorderBrushProperty,Brush.Parse("#24627C"))}});
        var root=new Grid{Margin=new Thickness(20),RowDefinitions=new RowDefinitions("Auto,*,Auto"),RowSpacing=14};
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*"),ColumnSpacing=16};
        header.Children.Add(new TextBlock{Text="▥",FontSize=44,Foreground=Cyan,VerticalAlignment=VerticalAlignment.Center});
        var heading=new StackPanel{Spacing=4};heading.Children.Add(new TextBlock{Text="AUDIO DOWNLOADER",FontSize=28,FontWeight=FontWeight.Bold,Foreground=Cyan});
        heading.Children.Add(new TextBlock{Text="BEN TOOLBOX  /  LOCAL AUDIO OPERATIONS",FontSize=11,Foreground=Muted});
        heading.Children.Add(new TextBlock{Text="YouTube → MP3 · Categorised queue · Native desktop",Foreground=Muted,FontSize=12});
        Grid.SetColumn(heading,1);header.Children.Add(heading);root.Children.Add(Panel(header));
        var columns=new Grid{ColumnDefinitions=new ColumnDefinitions("*,360"),RowDefinitions=new RowDefinitions("Auto,Auto"),ColumnSpacing=14,RowSpacing=14};Grid.SetRow(columns,1);root.Children.Add(new ScrollViewer{Content=columns,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});Grid.SetRow(root.Children[^1],1);
        editor.Children.Add(Field("01  /  VIDEO LINKS",url));
        var categories=new StackPanel{Orientation=Orientation.Horizontal,Spacing=24};categories.Children.Add(music);categories.Children.Add(other);
        var choices=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=16};choices.Children.Add(Field("02  /  DESTINATION CATEGORY",categories));var qualityField=Field("MP3 QUALITY",quality);Grid.SetColumn(qualityField,1);choices.Children.Add(qualityField);editor.Children.Add(choices);
        editor.Children.Add(new TextBlock{Text="Each added link remembers its category. Change it in the queue before starting.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,FontSize=12});
        var startActions=new WrapPanel{ItemSpacing=10,LineSpacing=8};
        startActions.Children.Add(ActionButton("+  ADD TO QUEUE",()=>{AddLinks();return Task.CompletedTask;},true));
        startActions.Children.Add(ActionButton("↓  DOWNLOAD QUEUE",DownloadQueue,true));editor.Children.Add(startActions);
        var folderRow=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=8};folderRow.Children.Add(destination);var browse=ActionButton("BROWSE",async()=>await Folder(destination));Grid.SetColumn(browse,1);folderRow.Children.Add(browse);editor.Children.Add(Field("BASE OUTPUT FOLDER",folderRow));
        editor.Children.Add(new TextBlock{Text="Music and Other are subfolders of this base. Defaults to your yt-dlp folder.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,FontSize=12});
        editor.Children.Add(summary);
        editor.Children.Add(new ScrollViewer{Content=rows,MaxHeight=235,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        var queueActions=new WrapPanel{ItemSpacing=8,LineSpacing=8};
        queueActions.Children.Add(ActionButton("CLEAR FINISHED",()=>{queue.RemoveAll(x=>x.State=="Complete");RebuildRows();return Task.CompletedTask;}));
        editor.Children.Add(queueActions);
        var left=new StackPanel{Spacing=14};left.Children.Add(Panel(editor));
        var display=new StackPanel{Spacing=12};var progressHeading=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=10};
        progressHeading.Children.Add(status);Grid.SetColumn(percent,1);progressHeading.Children.Add(percent);display.Children.Add(progressHeading);display.Children.Add(current);display.Children.Add(progress);
        var liveActions=new WrapPanel{ItemSpacing=12,LineSpacing=8};liveActions.Children.Add(cancel);
        liveActions.Children.Add(ActionButton("OPEN OUTPUT FOLDER",()=>{var folder=selectedItem?.Output is {} path?Path.GetDirectoryName(path):YouTubeAudioService.NormalizePath(destination.Text??"");if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Choose an existing output folder.");Process.Start(new ProcessStartInfo(folder!){UseShellExecute=true});return Task.CompletedTask;}));
        display.Children.Add(liveActions);columns.Children.Add(left);
        tools.Children.Add(new TextBlock{Text="⚙  TOOLS & TROUBLESHOOTING",Foreground=Cyan,FontWeight=FontWeight.Bold,FontSize=15});
        tools.Children.Add(Field("yt-dlp EXECUTABLE",executable));tools.Children.Add(ActionButton("SELECT yt-dlp.exe",async()=>
        {
            var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Choose your trusted yt-dlp executable",AllowMultiple=false});
            if(files.Count>0){executable.Text=files[0].TryGetLocalPath();standalone.IsChecked=false;}
        }));
        tools.Children.Add(Field("FFMPEG / FFPROBE FOLDER",ffmpeg));tools.Children.Add(ActionButton("SELECT FFMPEG FOLDER",async()=>await Folder(ffmpeg)));
        tools.Children.Add(new TextBlock{Text="Leave blank for auto-detection.",Foreground=Muted,FontSize=11});tools.Children.Add(standalone);
        tools.Children.Add(new TextBlock{Text="Pip / winget / unknown installation? Use its original package manager. Updates use the exact executable above.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,FontSize=12});
        tools.Children.Add(ActionButton("CHECK FOR UPDATES",()=>Run(token=>service.UpdateAsync(executable.Text??"",standalone.IsChecked==true,Log,token))));
        tools.Children.Add(ActionButton("CHECK DEPENDENCIES / VERSION",()=>Run(async token=>
        {
            Log("Executable: "+executable.Text);Log("yt-dlp version: "+await service.VersionAsync(executable.Text??"",token));
            var adjacent=Path.GetDirectoryName(executable.Text??"");
            Log("FFmpeg: "+(YouTubeAudioService.Find("ffmpeg",ffmpeg.Text)??YouTubeAudioService.Find("ffmpeg",adjacent)??"MISSING"));
            Log("ffprobe: "+(YouTubeAudioService.Find("ffprobe",ffmpeg.Text)??YouTubeAudioService.Find("ffprobe",adjacent)??"MISSING"));
            Log("Deno: "+(YouTubeAudioService.Find("deno",adjacent)??"Not detected; YouTube extraction may need Deno/EJS."));Log("EJS guidance: https://github.com/yt-dlp/yt-dlp/wiki/EJS");
        })));
        tools.Children.Add(new TextBlock{Text="LOCAL ENGINE\nyt-dlp + FFmpeg\n\nOne job at a time. Existing MP3s stay protected. No automatic binary installation.",Foreground=Muted,TextWrapping=TextWrapping.Wrap,FontSize=12});
        var right=Panel(tools);Grid.SetColumn(right,1);columns.Children.Add(right);
        var logPanel=new Expander{Header="≡  STATUS & LOG / DIAGNOSTICS",IsExpanded=false,HorizontalAlignment=HorizontalAlignment.Stretch,Content=diagnostics};var footerContent=new StackPanel{Spacing=8};footerContent.Children.Add(Panel(display));footerContent.Children.Add(logPanel);Grid.SetRow(footerContent,2);root.Children.Add(footerContent);Content=root;
        SizeChanged+=(_,_)=>{bool narrow=Bounds.Width<1000;columns.ColumnDefinitions=new ColumnDefinitions(narrow?"*":"*,360");Grid.SetColumn(right,narrow?0:1);Grid.SetRow(right,narrow?1:0);};
        Opened+=async(_,_)=>
        {
            editor.IsEnabled=tools.IsEnabled=false;var saved=await YouTubeAudioSettingsStore.LoadAsync();
            if(saved is not null)
            {
                if(!string.IsNullOrWhiteSpace(saved.Executable))executable.Text=YouTubeAudioService.NormalizePath(saved.Executable);
                ffmpeg.Text=saved.FfmpegFolder;quality.SelectedIndex=Math.Clamp(saved.Quality,0,3);
                // Old single-link destinations were arbitrary; categorised mode defaults beside the executable.
                destination.Text=saved.Categorized?saved.OutputFolder:Path.GetDirectoryName(executable.Text??"")??destination.Text;
                music.IsChecked=saved.Category!=AudioCategory.Other;other.IsChecked=!music.IsChecked;
            }
            editor.IsEnabled=tools.IsEnabled=true;
        };
        cancel.Click+=(_,_)=>active?.Cancel();timer.Tick+=(_,_)=>Flush();
        Closing+=(_,e)=>{if(active is not null){e.Cancel=true;closing=true;active.Cancel();}};Closed+=(_,_)=>timer.Stop();
    }
    private static Border Panel(Control content)=>new(){Padding=new Thickness(16),BoxShadow=BoxShadows.Parse("0 0 9 0 #1628CDED"),CornerRadius=new CornerRadius(3),BorderThickness=new Thickness(1),BorderBrush=Brush.Parse("#16748E"),Background=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),GradientStops=new GradientStops{new(Color.Parse("#10283B"),0),new(Color.Parse("#071321"),1)}},Child=content};
    private static Control Field(string label,Control control){var p=new StackPanel{Spacing=6};p.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=Muted,FontWeight=FontWeight.Bold});p.Children.Add(control);return p;}
    private Button ActionButton(string title,Func<Task> action,bool primary=false)
    {
        var button=new Button{Content=title,Foreground=primary?Cyan:Brushes.White,Background=Brush.Parse(primary?"#0C4058":"#10283A"),BorderBrush=Brush.Parse(primary?"#2ED6FA":"#20506A"),BorderThickness=new Thickness(1),Padding=new Thickness(12,9),HorizontalContentAlignment=HorizontalAlignment.Center};
        button.Click+=async(_,_)=>{try{await action();}catch(Exception ex){status.Text=ex.Message;}};return button;
    }
    private void AddLinks()
    {
        var added=YouTubeAudioQueue.Parse(url.Text??"",music.IsChecked==true?AudioCategory.Music:AudioCategory.Other);
        if(queue.Count+added.Length>YouTubeAudioQueue.MaximumItems)throw new InvalidOperationException("The queue holds up to 100 links. Clear finished items first.");
        queue.AddRange(added);url.Text="";RebuildRows();status.Text=$"READY · {added.Length} links added";
    }
    private void RebuildRows()
    {
        rows.Children.Clear();rowStates.Clear();
        foreach(var item in queue)
        {
            var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,95,Auto"),ColumnSpacing=8};
            var info=new StackPanel{Spacing=3};info.Children.Add(new TextBlock{Text=item.Url,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=11});var state=new TextBlock{Text=item.State,Foreground=Muted,FontSize=11,TextWrapping=TextWrapping.Wrap};info.Children.Add(state);rowStates[item]=state;row.Children.Add(info);
            var category=new ComboBox{ItemsSource=new[]{"Music","Other"},SelectedIndex=(int)item.Category,IsEnabled=item.State!="Complete"};category.SelectionChanged+=(_,_)=>{if(category.SelectedIndex>=0)item.Category=(AudioCategory)category.SelectedIndex;};Grid.SetColumn(category,1);row.Children.Add(category);
            var remove=ActionButton("×",()=>{queue.Remove(item);RebuildRows();return Task.CompletedTask;});Grid.SetColumn(remove,2);row.Children.Add(remove);rows.Children.Add(row);
        }
        UpdateSummary();
    }
    private void UpdateSummary()=>summary.Text=$"{queue.Count} ITEMS  /  {queue.Count(x=>x.State=="Complete")} COMPLETE  /  {queue.Count(x=>x.State=="Failed")} FAILED";
    private async Task DownloadQueue()
    {
        if(!string.IsNullOrWhiteSpace(url.Text))AddLinks();
        if(!queue.Any(x=>x.State!="Complete"))throw new InvalidOperationException("Add links to the queue first.");
        await Run(async token=>
        {
            var options=new YouTubeAudioOptions(executable.Text??"",ffmpeg.Text??"",destination.Text??"",new[]{"0","2","192K","128K"}[quality.SelectedIndex]);
            await YouTubeAudioQueue.RunAsync(queue.ToArray(),options.OutputFolder,(item,folder,changed,ct)=>service.DownloadAsync(options with{OutputFolder=folder},item.Url,changed,Log,ct),
                (item,s)=>
                {
                    // UI state is updated once per timer tick; only the latest progress is retained.
                    Interlocked.Exchange(ref latest,new UiProgress(item,s));
                    if(s.State=="Complete"||s.State=="Cancelled"||s.State.StartsWith("Failed"))Log(item.Category+" · "+s.State+(s.Output is null?"":" · "+s.Output));
                },token);
            Flush();UpdateSummary();status.Text=$"QUEUE FINISHED · {queue.Count(x=>x.State=="Complete")} complete / {queue.Count(x=>x.State=="Failed")} failed";
        });
    }
    private async Task Folder(TextBox box){var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{AllowMultiple=false});if(folders.Count>0)box.Text=folders[0].TryGetLocalPath();}
    private void Log(string message){messages.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");while(messages.Count>300)messages.TryDequeue(out _);}
    private void Flush()
    {
        while(messages.TryDequeue(out var message))diagnostics.Text+=message+Environment.NewLine;
        if((diagnostics.Text?.Length??0)>24000)diagnostics.Text=diagnostics.Text![^24000..];
        var update=Interlocked.Exchange(ref latest,null);
        if(update is not null)
        {
            var value=update.Status;selectedItem=update.Item;
            if(selectedItem.State is not ("Complete" or "Failed" or "Cancelled"))selectedItem.State=value.State;
            status.Text=value.State;current.Text=value.Output??selectedItem?.Url??"Local operation";progress.IsIndeterminate=value.Percent is null;progress.Value=value.Percent??0;percent.Text=value.Percent is {} n?$"{n:0}%":"—";
        }
        foreach(var item in queue)if(rowStates.TryGetValue(item,out var state))state.Text=item.State+(item.Error is null?"":" · "+item.Error);
        UpdateSummary();
    }
    private async Task Run(Func<CancellationToken,Task> operation)
    {
        if(active is not null)return;active=new();editor.IsEnabled=tools.IsEnabled=false;cancel.IsEnabled=true;progress.IsIndeterminate=true;percent.Text="—";status.Text="WORKING…";timer.Start();
        try
        {
            executable.Text=YouTubeAudioService.NormalizePath(executable.Text??"");
            try{await YouTubeAudioSettingsStore.SaveAsync(new(executable.Text??"",ffmpeg.Text??"",destination.Text??"",quality.SelectedIndex,true,music.IsChecked==true?AudioCategory.Music:AudioCategory.Other));}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Log("Could not remember preferences: "+ex.Message);}
            await operation(active.Token);Flush();if(status.Text=="WORKING…")status.Text="FINISHED · See diagnostics";
        }
        catch(OperationCanceledException){Flush();status.Text="CANCELLED · Remaining links stay queued. Download Queue retries unfinished items.";}
        catch(Exception ex){Flush();status.Text=ex.Message;}
        finally{active.Dispose();active=null;timer.Stop();editor.IsEnabled=tools.IsEnabled=true;cancel.IsEnabled=false;progress.IsIndeterminate=false;RebuildRows();if(closing)Close();}
    }
}
