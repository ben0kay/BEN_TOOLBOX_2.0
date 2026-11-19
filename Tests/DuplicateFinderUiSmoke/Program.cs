using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BEN_TOOLBOX_2_0;
using BEN_TOOLBOX_2_0.Services;
using BEN_TOOLBOX_2_0.Views;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false })
    .UseSkia().WithInterFont().SetupWithoutStarting();
var fixture=Path.Combine(Path.GetTempPath(),"btx-duplicates-"+Guid.NewGuid().ToString("N"));
var source=Path.Combine(fixture,"A");var destination=Path.Combine(fixture,"B");
Directory.CreateDirectory(source);Directory.CreateDirectory(destination);
var window=new DuplicateFinderWindow();
void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
void PumpUntil(Func<bool> done)
{
    var clock=System.Diagnostics.Stopwatch.StartNew();
    while(!done())
    {
        Check(clock.Elapsed<TimeSpan.FromSeconds(15),"Timed out waiting for Duplicate Finder");
        Dispatcher.UIThread.RunJobs();Thread.Sleep(10);
    }
    Dispatcher.UIThread.RunJobs();
}
IEnumerable<T> Controls<T>() where T : Control =>window.GetVisualDescendants().OfType<T>();
Button Button(string caption)=>Controls<Button>().Single(b=>Equals(b.Content,caption));
void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
try
{
    foreach(var root in new[]{source,destination})
    {
        File.WriteAllText(Path.Combine(root,"same.txt"),"same contents");
        Directory.CreateDirectory(Path.Combine(root,"nested"));
        File.WriteAllText(Path.Combine(root,"nested","nested.txt"),"nested match");
    }
    File.WriteAllText(Path.Combine(source,"different.txt"),"AAAA");
    File.WriteAllText(Path.Combine(destination,"different.txt"),"BBBB");
    window.Show();Dispatcher.UIThread.RunJobs();
    Check(window.Classes.Contains("btx-tool-window"),"Tool must opt into BTX styling");
    Check(window.Background is ISolidColorBrush brush && brush.Color==Color.Parse("#060F1C"),"Shared window resource did not load");
    var fields=Controls<TextBox>().Where(t=>t.Watermark is "Source A path" or "Destination B path").ToArray();
    Check(fields.Length==2&&fields.All(t=>t.Classes.Contains("btx-input")),"Styled path fields missing");
    Check(new[]{"SCAN","CANCEL","EXPORT CSV","RECYCLE SELECTED A","RECYCLE ALL VERIFIED A"}
        .All(caption=>Button(caption).Classes.Contains("btx-button")),"Unstyled tool action");
    Check(Controls<Button>().Count(b=>Equals(b.Content,"BROWSE"))==2,"Folder pickers missing");
    var scan=Button("SCAN");var recycle=Button("RECYCLE SELECTED A");var recycleAll=Button("RECYCLE ALL VERIFIED A");
    var export=Button("EXPORT CSV");var cancel=Button("CANCEL");
    Check(!recycle.IsEnabled&&!recycleAll.IsEnabled&&!export.IsEnabled&&!cancel.IsEnabled,"Initial action guards changed");
    fields.Single(t=>t.Watermark=="Source A path").Text=source;
    fields.Single(t=>t.Watermark=="Destination B path").Text=destination;
    Dispatcher.UIThread.RunJobs();
    var list=Controls<ListBox>().Single();
    Dispatcher.UIThread.RunJobs();
    Click(scan);PumpUntil(()=>scan.IsEnabled);
    var rows=list.ItemsSource!.Cast<DuplicateMatch>().ToArray();
    Check(rows.Length==2&&rows.Count(r=>r.Identical)==1,"Default hash scan changed");
    Check(export.IsEnabled&&recycleAll.IsEnabled&&!cancel.IsEnabled,"Completed scan actions changed");
    list.SelectedItem=rows.Single(r=>r.Identical);Dispatcher.UIThread.RunJobs();
    Check(recycle.IsEnabled,"Verified selection should enable recycling");
    list.SelectedItem=rows.Single(r=>!r.Identical);Dispatcher.UIThread.RunJobs();
    Check(!recycle.IsEnabled,"Unverified selection must not enable recycling");
    var hash=Controls<CheckBox>().Single(c=>Equals(c.Content,"Verify SHA-256 contents"));
    hash.IsChecked=false;Dispatcher.UIThread.RunJobs();
    Check(list.ItemsSource is null&&!export.IsEnabled&&!recycleAll.IsEnabled,"Option edit must invalidate results");
    Dispatcher.UIThread.RunJobs();
    Click(scan);PumpUntil(()=>scan.IsEnabled);
    Check(list.ItemsSource!.Cast<DuplicateMatch>().All(r=>r.Status=="NAME MATCH")&&!recycleAll.IsEnabled,"Name-only scan must not enable recycling");
    hash.IsChecked=true;
    Controls<CheckBox>().Single(c=>Equals(c.Content,"Include subfolders in A")).IsChecked=true;
    Dispatcher.UIThread.RunJobs();
    Click(scan);PumpUntil(()=>scan.IsEnabled);
    Check(list.ItemsSource!.Cast<DuplicateMatch>().Count()==3,"Recursive scan changed");
    window.Width=820;window.Height=620;Dispatcher.UIThread.RunJobs();
    var outer=(ScrollViewer)window.Content!;
    Check(outer.Extent.Height>outer.Viewport.Height,"Narrow window must keep content scrollable");
    using(var bitmap=new RenderTargetBitmap(new PixelSize(820,620)))
    {
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Path.GetTempPath(),"btx-duplicate-preview.png"));
    }
    outer.Offset=new Vector(0,outer.Extent.Height);Dispatcher.UIThread.RunJobs();
    Check(outer.Offset.Y>0,"Outer scrolling failed");
    fields[0].Text="";Dispatcher.UIThread.RunJobs();
    Check(list.ItemsSource is null&&!recycle.IsEnabled&&!recycleAll.IsEnabled,"Path edit must invalidate results");
    Check(File.ReadAllText(Path.Combine(source,"different.txt"))=="AAAA"&&
        File.ReadAllText(Path.Combine(destination,"different.txt"))=="BBBB","Read-only test changed fixture files");
    Console.WriteLine("Duplicate Finder BTX resource, layout and scan/selection/invalidation smoke passed.");
}
finally
{
    window.Close();Dispatcher.UIThread.RunJobs();Directory.Delete(fixture,true);
}
