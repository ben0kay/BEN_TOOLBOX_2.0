using System.Net;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using BEN_TOOLBOX_2_0;
using BEN_TOOLBOX_2_0.Services;
using BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
void Pump(Func<bool> complete){var watch=Stopwatch.StartNew();while(!complete()){Dispatcher.UIThread.RunJobs();if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("UI operation timed out");Thread.Sleep(5);}Dispatcher.UIThread.RunJobs();}
void Check(bool b,string message){if(!b)throw new Exception(message);}
Check(ToolCatalog.ByCategory("Inspect").Any(t=>t.Id=="internet-observatory"&&t.Status.Contains("INTERNET DATA")),"Native catalog status");
var root=Path.Combine(Path.GetTempPath(),"observatory-ui-"+Guid.NewGuid());Directory.CreateDirectory(root);
try
{
    var handler=new FeedHandler();var window=new InternetObservatoryWindow(new UsgsEarthquakeService(root,handler),true,root);window.Show();var map=window.GetVisualDescendants().OfType<WorldMapView>().Single();var list=window.GetVisualDescendants().OfType<ListBox>().Single();Pump(()=>list.ItemCount==2);Check(map.Bounds.Width>400&&map.Bounds.Height>=300,"Useful map area missing");list.SelectedIndex=0;Dispatcher.UIThread.RunJobs();Check(map.SelectedId is not null,"Map/list selection");var chosen=(Earthquake)list.SelectedItem!;Check(map.HitTestEvent(map.Project(chosen.Longitude,chosen.Latitude))?.Id==chosen.Id,"Pin hit test");
    var preset=window.GetVisualDescendants().OfType<ComboBox>().Single();preset.SelectedIndex=1;Pump(()=>handler.Calls>=2&&list.ItemCount==1);Check(((Earthquake)list.ItemsSource!.Cast<object>().Single()).Id=="week","Wrong preset result");var slider=window.GetVisualDescendants().OfType<Slider>().Single();slider.Value=8;Dispatcher.UIThread.RunJobs();Check(list.ItemCount==0,"Local magnitude filter");slider.Value=0;Dispatcher.UIThread.RunJobs();
    using(var bitmap=new RenderTargetBitmap(new PixelSize(1440,940))){bitmap.Render(window);bitmap.Save("/tmp/observatory-ui.png");}
    window.Width=950;window.Height=750;Dispatcher.UIThread.RunJobs();Check(map.Bounds.Width>400&&map.Bounds.Height>=300,"Compact map collapsed");map.Projection.Set(2,20,10);window.Close();Pump(()=>!window.IsVisible);Check(handler.Disposed,"Client disposal");var savedTask=ObservatorySettingsStore.LoadAsync(root);Pump(()=>savedTask.IsCompleted);Check(savedTask.Result.Preset==1&&savedTask.Result.Zoom==2,"Saved view");
    var switchHandler=new FeedHandler{SlowFirst=true};var switchWindow=new InternetObservatoryWindow(new UsgsEarthquakeService(root,switchHandler),true,root);switchWindow.Show();Pump(()=>switchHandler.Calls>0);switchWindow.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex=0;var switchList=switchWindow.GetVisualDescendants().OfType<ListBox>().Single();Pump(()=>switchHandler.Calls>=2&&switchList.ItemCount==2);Check(switchHandler.Cancelled,"Preset switch cancellation");switchWindow.Close();Pump(()=>!switchWindow.IsVisible);
    var slowHandler=new FeedHandler{Slow=true};var slowWindow=new InternetObservatoryWindow(new UsgsEarthquakeService(root,slowHandler),true,root);slowWindow.Show();Pump(()=>slowHandler.Calls>0);slowWindow.Close();Pump(()=>!slowWindow.IsVisible);Check(slowHandler.Cancelled&&slowHandler.Disposed,"Close during request did not cancel/dispose");
    Console.WriteLine("PASS: native window/catalog/embedded geography, pins/list/filter/presets, hit testing, compact layout, saved view, switching/close cancellation.");
}
finally{Directory.Delete(root,true);}
sealed class FeedHandler:HttpMessageHandler
{
    public int Calls;public bool Disposed,Slow,SlowFirst,Cancelled;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Interlocked.Increment(ref Calls);if(Slow||(SlowFirst&&Calls==1)){try{await Task.Delay(10000,token);}catch(OperationCanceledException){Cancelled=true;throw;}}
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();bool week=request.RequestUri!.AbsolutePath.Contains("week");
        string Event(string id,int lon,int lat,int mag)=>$"{{\"id\":\"{id}\",\"properties\":{{\"mag\":{mag},\"place\":\"Fixture {id}\",\"time\":{now},\"updated\":{now},\"tsunami\":0}},\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{lon},{lat},20]}}}}";
        var json=$"{{\"type\":\"FeatureCollection\",\"metadata\":{{\"generated\":{now}}},\"features\":["+(week?Event("week",140,36,5):Event("pacific",-122,38,4)+","+Event("asia",130,32,5))+"]}";return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    }
    protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
}
