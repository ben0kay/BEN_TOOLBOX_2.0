using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
namespace BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
public sealed class InternetObservatoryWindow:Window
{
    private readonly UsgsEarthquakeService service;private readonly bool startNetwork;private readonly string? settingsDirectory;
    private readonly WorldMapView map=new();
    private readonly ComboBox preset=new(){ItemsSource=QuakeFeed.Presets,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly Slider magnitude=new(){Minimum=0,Maximum=9,TickFrequency=.5,IsSnapToTickEnabled=true};
    private readonly CheckBox visibleOnly=new(){Content="Only events inside map view"},auto=new(){Content="Auto refresh · 2 minutes",IsChecked=true};
    private readonly ListBox list=new(){MinHeight=140,MaxHeight=340};
    private readonly TextBlock status=Text("NOT LOADED",16),freshness=Text("No USGS data retrieved yet."),coverage=Text(""),counts=Text(""),details=Text("Select a pin or list event."),filterLabel=Text("Minimum magnitude: all"),mapInfo=Text("Wheel zoom · drag pan · Home fits world · arrows pan · +/- zoom");
    private readonly Button refresh=new(){Content="REFRESH NOW"},source=new(){Content="OPEN USGS REPORT",IsEnabled=false};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(15)};
    private readonly HashSet<Task> requests=new();
    private CancellationTokenSource? request;private QuakeResult? result;private Earthquake? selected;
    private DateTimeOffset nextRefresh=DateTimeOffset.MinValue;private bool ready,closed,closing,applying;private int generation;
    public InternetObservatoryWindow(UsgsEarthquakeService? service=null,bool startNetwork=true,string? settingsDirectory=null)
    {
        this.service=service??new UsgsEarthquakeService(settingsDirectory);this.startNetwork=startNetwork;this.settingsDirectory=settingsDirectory;
        Title="BEN TOOLBOX / INTERNET OBSERVATORY";Width=1440;Height=940;MinWidth=900;MinHeight=700;Background=Brush.Parse("#07111F");
        var root=new Grid{Margin=new Thickness(18),RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto"),RowSpacing=12};
        var heading=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=12};var title=new StackPanel{Spacing=4};title.Children.Add(Text("INTERNET OBSERVATORY",27));title.Children.Add(Text("EARTHQUAKE EXPLORER / REAL USGS DATA / UTC",11));heading.Children.Add(title);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,VerticalAlignment=VerticalAlignment.Center};actions.Children.Add(auto);actions.Children.Add(refresh);Grid.SetColumn(actions,1);heading.Children.Add(actions);root.Children.Add(heading);
        var health=new StackPanel{Spacing=5};health.Children.Add(status);health.Children.Add(freshness);var healthPanel=Panel(health);Grid.SetRow(healthPanel,1);root.Children.Add(healthPanel);
        var columns=new Grid{ColumnDefinitions=new ColumnDefinitions("*,335"),RowDefinitions=new RowDefinitions("*"),ColumnSpacing=14};Grid.SetRow(columns,2);root.Children.Add(columns);
        var mapPanel=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),RowSpacing=10};var mapHeading=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};mapHeading.Children.Add(Text("EARTHQUAKES / EQUIRECTANGULAR WORLD MAP",12));var fit=Button("FIT WORLD",()=>map.Fit());Grid.SetColumn(fit,1);mapHeading.Children.Add(fit);mapPanel.Children.Add(mapHeading);Grid.SetRow(map,1);mapPanel.Children.Add(map);
        var legend=new StackPanel{Spacing=5};legend.Children.Add(Text("Depth: cyan <70 km · amber 70–299 km · violet ≥300 km · gray unknown",11));legend.Children.Add(Text("Circle size = magnitude (M? uses fixed size) · white ring = selection",11));legend.Children.Add(mapInfo);Grid.SetRow(legend,2);mapPanel.Children.Add(legend);columns.Children.Add(Panel(mapPanel));
        var side=new StackPanel{Spacing=10};side.Children.Add(Text("EARTHQUAKES",18));side.Children.Add(Text("Space Weather IO3 · Website Watch IO4 · Network IO5\nPlanned — not active modules",11));side.Children.Add(preset);side.Children.Add(coverage);side.Children.Add(filterLabel);side.Children.Add(magnitude);side.Children.Add(visibleOnly);side.Children.Add(counts);
        list.ItemTemplate=new FuncDataTemplate<Earthquake>((e,_)=>Text(e?.ToString()??"",12));side.Children.Add(list);side.Children.Add(Text("SELECTED EVENT",12));side.Children.Add(details);side.Children.Add(source);var sidePanel=Panel(new ScrollViewer{Content=side,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});Grid.SetColumn(sidePanel,1);columns.Children.Add(sidePanel);
        var foot=new StackPanel{Spacing=4};foot.Children.Add(Text("Source: earthquake.usgs.gov · Made with Natural Earth (public domain) · No safety alerts or instantaneous coverage implied.",11));foot.Children.Add(Button("USGS FEED DOCUMENTATION",()=>Open("https://earthquake.usgs.gov/earthquakes/feed/v1.0/geojson.php")));Grid.SetRow(foot,3);root.Children.Add(foot);Content=root;
        SizeChanged+=(_,_)=>{bool narrow=Bounds.Width<1120;columns.ColumnDefinitions=new ColumnDefinitions(narrow?"*":"*,335");columns.RowDefinitions=new RowDefinitions(narrow?"*,230":"*");Grid.SetColumn(sidePanel,narrow?0:1);Grid.SetRow(sidePanel,narrow?1:0);};
        refresh.Click+=async(_,_)=>await RefreshAsync();preset.SelectionChanged+=async(_,_)=>{if(!ready)return;result=null;selected=null;ApplyFilters();await RefreshAsync();};magnitude.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty&&ready)ApplyFilters();};visibleOnly.IsCheckedChanged+=(_,_)=>{if(ready)ApplyFilters();};
        map.ViewChanged+=()=>{mapInfo.Text=$"Zoom {map.Projection.Zoom:0.0}× · wheel zoom / drag pan · Home fits world";if(ready&&visibleOnly.IsChecked==true)ApplyFilters();};map.EventSelected+=e=>Select(e,true);list.SelectionChanged+=(_,_)=>{if(!applying&&list.SelectedItem is Earthquake e)Select(e,false);};source.Click+=(_,_)=>{if(UsgsEarthquakeService.SafeEventUrl(selected?.Url) is {} url)Open(url);};
        timer.Tick+=async(_,_)=>{UpdateFreshness();if(CanRefresh&&startNetwork&&auto.IsChecked==true&&DateTimeOffset.UtcNow>=nextRefresh&&request is null)await RefreshAsync();};
        PropertyChanged+=(_,e)=>{if(e.Property==WindowStateProperty||e.Property==IsVisibleProperty){if(CanRefresh&&ready){timer.Start();UpdateFreshness();}else{timer.Stop();generation++;request?.Cancel();if(ready&&!closed)status.Text="PAUSED · window hidden or minimised";}}};
        Opened+=async(_,_)=>await InitializeAsync();Closing+=OnClosing;
    }
    private bool CanRefresh=>!closed&&IsVisible&&WindowState!=WindowState.Minimized;
    private async Task InitializeAsync()
    {
        var s=await ObservatorySettingsStore.LoadAsync(settingsDirectory);if(closed)return;preset.SelectedIndex=s.Preset;magnitude.Value=s.MinimumMagnitude;visibleOnly.IsChecked=s.VisibleOnly;auto.IsChecked=s.AutoRefresh;map.Projection.Set(s.Zoom,s.CenterLongitude,s.CenterLatitude);map.InvalidateVisual();ready=true;ApplyFilters();timer.Start();if(startNetwork)await RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        if(!ready||!CanRefresh)return;request?.Cancel();var owner=new CancellationTokenSource();request=owner;var task=RefreshCore(preset.SelectedIndex,++generation,owner);requests.Add(task);try{await task;}finally{requests.Remove(task);}
    }
    private async Task RefreshCore(int index,int version,CancellationTokenSource owner)
    {
        var token=owner.Token;refresh.IsEnabled=false;status.Text="LOADING · USGS";nextRefresh=DateTimeOffset.UtcNow.AddMinutes(2);
        try
        {
            if(result is null){var cached=await service.LoadCachedAsync(index,token);if(version!=generation||closed)return;result=cached;ApplyFilters();status.Text="LOADING · checking USGS (cached map retained)";}
            var fetched=await service.RefreshAsync(index,token);if(version!=generation||closed)return;result=fetched.Snapshot is null&&fetched.Error is not null?fetched with{Snapshot=result?.Snapshot}:fetched;ApplyFilters();
        }
        catch(OperationCanceledException){if(version==generation&&!closed)status.Text="PAUSED · data retained";}
        catch(Exception ex){if(version==generation&&!closed){result=new(result?.Snapshot,ex.Message,true);UpdateFreshness();}}
        finally{if(ReferenceEquals(request,owner)){request=null;if(!closed)refresh.IsEnabled=true;}owner.Dispose();}
    }
    private void ApplyFilters()
    {
        if(applying)return;applying=true;try
        {
            var feed=QuakeFeed.Presets[Math.Clamp(preset.SelectedIndex,0,2)];coverage.Text=$"{feed.Coverage}\nFeed: {feed.File}.geojson";filterLabel.Text=magnitude.Value<=0?"Minimum magnitude: no extra filter (includes M?)":$"Minimum magnitude: {magnitude.Value:0.0} (unknown excluded)";
            var all=result?.Snapshot?.Events??Array.Empty<Earthquake>();var filtered=all.Where(e=>(magnitude.Value<=0||e.Magnitude>=magnitude.Value)&&(visibleOnly.IsChecked!=true||map.Projection.Contains(e))).ToArray();counts.Text=$"{filtered.Length} matching / {all.Length} in feed\n{filtered.Count(map.Projection.Contains)} in map view · {result?.Snapshot?.Skipped??0} invalid records skipped";
            string? id=selected?.Id;list.ItemsSource=filtered;map.SetEvents(filtered);selected=filtered.FirstOrDefault(e=>e.Id==id);list.SelectedItem=selected;ShowDetails();UpdateFreshness();
        }finally{applying=false;}
    }
    private void Select(Earthquake e,bool fromMap)
    {
        selected=e;if(!fromMap&&!map.Projection.Contains(e)){map.Projection.Set(map.Projection.Zoom,e.Longitude,e.Latitude);map.InvalidateVisual();}if(fromMap){applying=true;list.SelectedItem=e;list.ScrollIntoView(e);applying=false;}ShowDetails();
    }
    private void ShowDetails()
    {
        map.SelectedId=selected?.Id;map.InvalidateVisual();source.IsEnabled=selected?.Url is not null;details.Text=selected is {} e?$"{e.Place}\nMagnitude: {e.Magnitude?.ToString("0.0")??"not reported"}\nDepth: {e.DepthKm?.ToString("0.0")??"unknown"} km\nCoordinates: {e.Latitude:0.000}°, {e.Longitude:0.000}°\nEvent: {e.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC\nUpdated: {e.UpdatedUtc:yyyy-MM-dd HH:mm:ss} UTC\nTsunami flag: {(e.Tsunami is null?"not reported":e.Tsunami.Value?"1 (see USGS)":"0")}\nUSGS ID: {e.Id}":"Select a pin or list item. No selection is retained when filtered out.";
    }
    private void UpdateFreshness()
    {
        if(result is null){freshness.Text="No USGS data retrieved yet.";return;}var now=DateTimeOffset.UtcNow;status.Foreground=Brush.Parse(result.IsStale(now)?"#F0BC68":"#4BDACF");status.Text=(result.Snapshot is null?"UNAVAILABLE · USGS":result.IsStale(now)?"STALE / OFFLINE CACHE":"FRESH · USGS")+(result.Error is null?"":" · "+result.Error);
        if(result.Snapshot is {} s){double age=Math.Max(0,(now-s.FetchedUtc).TotalMinutes);freshness.Text=$"Fetched {(age<1?"less than a minute":$"{age:0} minutes")} ago ({s.FetchedUtc:yyyy-MM-dd HH:mm:ss} UTC)\nProvider generated: {s.ProviderUtc:yyyy-MM-dd HH:mm:ss} UTC · {result.NewEvents} new / {result.UpdatedEvents} updated since prior snapshot";}else freshness.Text="No cached events available. The offline basemap remains usable; an empty map is not a successful feed check.";
    }
    private async void OnClosing(object? sender,WindowClosingEventArgs e)
    {
        if(closing)return;e.Cancel=true;closing=true;closed=true;ready=false;generation++;timer.Stop();request?.Cancel();
        try{await ObservatorySettingsStore.SaveAsync(new(Preset:preset.SelectedIndex,MinimumMagnitude:magnitude.Value,VisibleOnly:visibleOnly.IsChecked==true,AutoRefresh:auto.IsChecked==true,Zoom:map.Projection.Zoom,CenterLongitude:map.Projection.CenterLongitude,CenterLatitude:map.Projection.CenterLatitude),settingsDirectory);}
        catch(Exception ex){Debug.WriteLine("Could not save Observatory preferences: "+ex.Message);}
        try{await Task.WhenAll(requests.ToArray());}finally{service.Dispose();Close();}
    }
    private static TextBlock Text(string text,int size=12)=>new(){Text=text,FontSize=size,Foreground=Brush.Parse(size>=16?"#56DBF2":"#9EBBCD"),TextWrapping=TextWrapping.Wrap};
    private static Border Panel(Control child)=>new(){Child=child,Padding=new Thickness(12),BorderThickness=new Thickness(1),BorderBrush=Brush.Parse("#1D6079"),Background=Brush.Parse("#0A1B2B"),CornerRadius=new CornerRadius(3)};
    private static Button Button(string text,Action action){var b=new Button{Content=text};b.Click+=(_,_)=>action();return b;}
    private void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){status.Text=ex.Message;}}
}
