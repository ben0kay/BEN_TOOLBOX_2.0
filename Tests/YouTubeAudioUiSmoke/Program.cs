using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using BEN_TOOLBOX_2_0;
using BEN_TOOLBOX_2_0.Views;
using BEN_TOOLBOX_2_0.Services;
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
var tool=ToolCatalog.ByCategory("Files").SingleOrDefault(t=>t.Id=="youtube-audio-downloader");
if(tool is null || tool.Name!="Audio Downloader" || !tool.Description.Contains("SoundCloud") || !tool.Status.StartsWith("NATIVE AVALONIA"))throw new Exception("Audio Downloader catalog label/status missing");
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
if(window.GetVisualDescendants().OfType<TextBlock>().Count(t=>t.Text?.Contains("soundcloud.com/artist/two")==true)<1)
    throw new Exception("Right queue status entries missing");
using(var bitmap=new RenderTargetBitmap(new PixelSize(1220,940))) { bitmap.Render(window);bitmap.Save("/tmp/youtube-queue-preview.png"); }
window.Width=700;window.Height=650;Dispatcher.UIThread.RunJobs();
var tools=Button("CHECK FOR UPDATES");if(tools.Bounds.Width<=0)throw new Exception("Compact controls collapsed");
if(Grid.GetRow(toolsPanel)!=2 || Grid.GetRow(queuePanel)!=3 || Grid.GetColumn(queuePanel)!=0)
    throw new Exception("Narrow layout must stack setup, READY, Tools and Queue Status");
window.Close();Dispatcher.UIThread.RunJobs();
Console.WriteLine("PASS: Audio Downloader compact tools, co-aligned READY/Queue Status, compact browse rows, dependencies, stacked narrow layout and clean close.");
