using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using BEN_TOOLBOX_2_0;
using BEN_TOOLBOX_2_0.Views;
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia().SetupWithoutStarting();
void Check(bool b,string message){if(!b)throw new Exception(message);}
var window=new MainWindow();window.Show();Dispatcher.UIThread.RunJobs();
T Find<T>(string name) where T:Control=>window.FindControl<T>(name)??throw new Exception(name+" missing");
var dashboard=Find<StackPanel>("DashboardPanel");var clock=Find<TextBlock>("DashboardTimeText");
Check(!clock.GetVisualAncestors().Contains(dashboard),"Clock is still inside dashboard content");
Check(!window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="DESKTOP INTERFACE ACTIVE"),"Redundant command strip remains");
foreach(var size in new[]{new PixelSize(1440,1000),new PixelSize(900,700),new PixelSize(1920,1080)})
{
    window.Width=size.Width;window.Height=size.Height;Dispatcher.UIThread.RunJobs();
    foreach(var name in new[]{"GameTile","FilesTile","InspectTile","SettingsTile"}){var tile=Find<Button>(name);Check(tile.Bounds.Width>150&&tile.Bounds.Width<=301,"Module cards stretched or collapsed");}
    var wrap=Find<WrapPanel>("DashboardModulesWrap");Check(wrap.Bounds.Width>0,"Module layout missing");
    using var image=new RenderTargetBitmap(size);image.Render(window);image.Save($"/tmp/dashboard-{size.Width}.png");
}
var before=clock.Text;Find<Button>("GameTile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Check(!dashboard.IsVisible&&Find<TextBlock>("PageTitle").Text=="Game Development","Module navigation broken");
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var timer=(DispatcherTimer)typeof(MainWindow).GetField("_dashboardTimer",flags)!.GetValue(window)!;
Check(timer.IsEnabled,"Shell clock timer stopped outside dashboard");
Check(!(bool)typeof(MainWindow).GetField("_dashboardActive",flags)!.GetValue(window)!,"Telemetry stayed active outside dashboard");
clock.Text="clock test";typeof(MainWindow).GetMethod("DashboardClockTick",flags)!.Invoke(window,new object?[]{null,EventArgs.Empty});
Check(clock.Text!="clock test","Top HUD clock tick did not update outside dashboard");
Find<Button>("DashboardNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Check(dashboard.IsVisible,"Dashboard return broken");window.Close();Dispatcher.UIThread.RunJobs();Check(!timer.IsEnabled,"Clock timer survived window close");
Console.WriteLine("PASS: compiled XAML/main window, top HUD clock, compact wrapping cards at 900/1440/1920, module navigation, clock across sections and clean close.");
