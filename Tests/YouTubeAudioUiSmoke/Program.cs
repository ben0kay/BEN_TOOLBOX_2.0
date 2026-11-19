using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BEN_TOOLBOX_2_0;
using BEN_TOOLBOX_2_0.Views;
using BEN_TOOLBOX_2_0.Services;
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().WithInterFont().SetupWithoutStarting();
var tool=ToolCatalog.ByCategory("Files").SingleOrDefault(t=>t.Id=="youtube-audio-downloader");
if(tool is null || tool.Name!="Audio Downloader" || !tool.Description.Contains("SoundCloud") || !tool.Status.StartsWith("NATIVE AVALONIA"))throw new Exception("Audio Downloader catalog label/status missing");
void PumpUntil(Func<bool> done)
{
    var timeout=System.Diagnostics.Stopwatch.StartNew();
    while(!done())
    {
        if(timeout.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Timed out waiting for queue UI");
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
}
var window=new YouTubeAudioDownloaderWindow();window.Show();Dispatcher.UIThread.RunJobs();
Button Button(string label)=>window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,label));
_ = Button("CHECK FOR UPDATES");
if(window.GetVisualDescendants().OfType<RadioButton>().Count()!=2)throw new Exception("Music/Other selector missing");
var input=window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn&&!t.IsReadOnly);
input.Text="https://youtu.be/one\nhttps://soundcloud.com/artist/two";
Button("+  ADD TO QUEUE").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Dispatcher.UIThread.RunJobs();
if(input.Text!=""||window.GetVisualDescendants().OfType<ComboBox>().Count()!=3)throw new Exception("Multi-link queue rows missing");
var readyPanel=window.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="AudioLiveStatusPanel");
var queuePanel=window.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="AudioQueueStatusPanel");
var editorPanel=window.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="AudioEditorPanel");
var toolsPanel=window.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="AudioToolsPanel");
if(readyPanel.Parent is not Grid parent || !ReferenceEquals(queuePanel.Parent,parent) ||
   Grid.GetRow(readyPanel)!=1 || Grid.GetRow(queuePanel)!=1 ||
   Grid.GetColumn(readyPanel)!=0 || Grid.GetColumn(queuePanel)!=1 ||
   Grid.GetRow(editorPanel)!=0 || Grid.GetRow(toolsPanel)!=0)
    throw new Exception("Desktop READY and Queue Status panels must align in row 1 under setup/tools");
var browserButtons=window.GetVisualDescendants().OfType<Button>().Where(b=>Equals(b.Content,"BROWSE")).ToArray();
if(browserButtons.Length<3 || !Equals(Button("CHECK TOOLS / VERSION").Content,"CHECK TOOLS / VERSION"))
    throw new Exception("Both compact dependency selectors and version check must remain accessible");
ScrollViewer QueueScroll()=>window.GetVisualDescendants().OfType<ScrollViewer>().Single(view=>view.Name=="AudioQueueScroll");
if(!QueueScroll().GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="https://soundcloud.com/artist/two") ||
   queuePanel.GetVisualDescendants().OfType<TextBlock>().Any())
    throw new Exception("Queue entries must appear in the main list and the placeholder must be blank");
using(var bitmap=new RenderTargetBitmap(new PixelSize(1220,940))) { bitmap.Render(window);bitmap.Save("/tmp/youtube-queue-preview.png"); }
window.Width=700;window.Height=650;Dispatcher.UIThread.RunJobs();
var tools=Button("CHECK FOR UPDATES");if(tools.Bounds.Width<=0)throw new Exception("Compact controls collapsed");
if(Grid.GetRow(toolsPanel)!=2 || Grid.GetRow(queuePanel)!=3 || Grid.GetColumn(queuePanel)!=0)
    throw new Exception("Narrow layout must stack setup, READY, Tools and Queue Status");
window.Close();Dispatcher.UIThread.RunJobs();
if(!OperatingSystem.IsWindows())
{
    var fixture=Path.Combine(Path.GetTempPath(),"btx-audio-ui-"+Guid.NewGuid().ToString("N"));
    var previousData=Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    Directory.CreateDirectory(fixture);
    try
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME",fixture);
        if(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)!=fixture)
            throw new Exception("Could not isolate audio test preferences");
        var exe=Path.Combine(fixture,"yt-dlp");
        File.WriteAllText(exe,"""
#!/usr/bin/env python3
import json, os, sys, time
args=sys.argv[1:]
if '--flat-playlist' in args:
    print(json.dumps({'id':'abc','title':'Playlist fixture song'}))
    sys.exit(0)
home=next(p[5:] for p in args if p.startswith('home:'))
if 'soundcloud.com' in args[-1] and 'titled' not in args[-1]:
    print('[download] has already been recorded in the archive')
    sys.exit(0)
if 'fail' in args[-1]: sys.exit(2)
resume=os.path.join(os.path.dirname(__file__),'resume')
deadline=time.monotonic()+15
while not os.path.exists(resume):
    if time.monotonic()>deadline: sys.exit(3)
    time.sleep(.01)
title='SoundCloud — \"雪\" live' if 'soundcloud.com' in args[-1] else 'YouTube — \"雪\" live'
file=os.path.join(home,('SoundCloud' if 'soundcloud.com' in args[-1] else 'Solo' if 'solo' in args[-1] else 'Fixture')+'.mp3')
with open(file,'xb') as output: output.write(b'fixture')
print('BTX_FILE:'+json.dumps(file))
if 'solo' in args[-1] or 'titled' in args[-1]: print('BTX_TITLE:'+json.dumps(title))
""");
        File.SetUnixFileMode(exe,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(fixture,"ffmpeg"),"");
        File.WriteAllText(Path.Combine(fixture,"ffprobe"),"");
        Task.Run(()=>YouTubeAudioSettingsStore.SaveAsync(new(exe,fixture,fixture,0,true))).GetAwaiter().GetResult();
        window=new YouTubeAudioDownloaderWindow();
        window.Show();
        PumpUntil(()=>Button("↓  DOWNLOAD QUEUE").IsEffectivelyEnabled);
        input=window.GetVisualDescendants().OfType<TextBox>().Single(t=>t.AcceptsReturn&&!t.IsReadOnly);
        input.Text="https://youtube.com/playlist?list=PL123\nhttps://youtu.be/solo\nhttps://soundcloud.com/artist/titled\nhttps://youtu.be/fail\n"+
            string.Join("\n",Enumerable.Range(1,10).Select(i=>$"https://soundcloud.com/artist/track-{i}"));
        Button("↓  DOWNLOAD QUEUE").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        var scroll=QueueScroll();
        TextBlock? QueueLabel(string text)=>scroll.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t=>t.Text==text);
        PumpUntil(()=>QueueLabel("Playlist fixture song") is not null&&scroll.Extent.Height>scroll.Viewport.Height);
        if(!scroll.IsEffectivelyEnabled||Button("CLEAR FINISHED").IsEffectivelyEnabled||
           scroll.GetVisualDescendants().OfType<ComboBox>().Any(box=>box.IsEffectivelyEnabled)||
           scroll.GetVisualDescendants().OfType<Button>().Any(button=>Equals(button.Content,"×")&&button.IsEffectivelyEnabled))
            throw new Exception("The active queue must allow scrolling while edits are disabled");
        scroll.Offset=new Vector(0,scroll.Extent.Height-scroll.Viewport.Height);
        if(scroll.Offset.Y<=0)throw new Exception("Could not scroll the active queue");
        File.WriteAllText(Path.Combine(fixture,"resume"),"");
        PumpUntil(()=>Button("↓  DOWNLOAD QUEUE").IsEffectivelyEnabled);
        bool HasColor(string label,string color)=>QueueLabel(label)?.Foreground is ISolidColorBrush brush&&brush.Color==Color.Parse(color);
        if(!HasColor("Complete","#73E6AB")||!HasColor("Failed","#FF8695")||QueueLabel("Skipped") is null||
           !window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("3 COMPLETE  /  10 SKIPPED  /  1 FAILED  /  0 REMAINING")==true))
            throw new Exception("Expanded queue states, colors or summary were not rendered");
        if(QueueLabel("YouTube — \"雪\" live") is null||QueueLabel("SoundCloud — \"雪\" live") is null)
            throw new Exception("Single-track Unicode/quoted metadata titles missing");
        var indicators=scroll.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Name=="AudioQueueOutcome").ToArray();
        if(indicators.Count(t=>t.Text=="✓"&&t.Foreground is ISolidColorBrush b&&b.Color==Color.Parse("#73E6AB"))!=3||
           indicators.Count(t=>t.Text=="✗"&&t.Foreground is ISolidColorBrush b&&b.Color==Color.Parse("#FF8695"))!=1||
           indicators.Any(t=>Grid.GetColumn(t)!=1||t.Parent is not Grid row||
               !row.Children.OfType<ComboBox>().Any(c=>Grid.GetColumn(c)==2)))
            throw new Exception("Outcome icons must precede category and match actual complete/failed states");
        if(scroll.Offset.Y<=0)throw new Exception("Finishing the queue reset its scroll position");
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME",previousData);
        Directory.Delete(fixture,true);
    }
}
Console.WriteLine("PASS: Audio Downloader unified queue, scrolling during downloads, status colors, compact tools and responsive panel layout.");
