using System;
using System.Linq;
namespace BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
public sealed record Earthquake(string Id,double? Magnitude,string Place,DateTimeOffset TimeUtc,DateTimeOffset UpdatedUtc,double Longitude,double Latitude,double? DepthKm,bool? Tsunami,string? Url)
{
    public override string ToString()=>$"M{Magnitude?.ToString("0.0")??"?"} · {Place}\n{TimeUtc:MMM dd HH:mm} UTC";
}
public sealed record QuakeFeed(string Name,string File,string Coverage)
{
    public Uri Uri=>new("https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/"+File+".geojson");
    public static readonly QuakeFeed[] Presets={new("Past 24 hours","2.5_day","M2.5+ · rolling 24 hours"),new("Past week","4.5_week","M4.5+ · rolling 7 days"),new("Significant events","significant_month","USGS significant events · rolling 30 days")};
    public override string ToString()=>Name;
}
public sealed record QuakeSnapshot(Earthquake[] Events,DateTimeOffset ProviderUtc,DateTimeOffset FetchedUtc,string? ETag=null,DateTimeOffset? LastModified=null,int Skipped=0);
public sealed record QuakeResult(QuakeSnapshot? Snapshot,string? Error=null,bool FromCache=false,int NewEvents=0,int UpdatedEvents=0)
{
    public bool IsStale(DateTimeOffset now)=>Error is not null||FromCache||Snapshot is null||now-Snapshot.FetchedUtc>TimeSpan.FromMinutes(5)||now-Snapshot.ProviderUtc>TimeSpan.FromMinutes(10);
}
public sealed record ObservatorySettings(int Version=1,string Page="Earthquakes",int Preset=0,double MinimumMagnitude=0,bool VisibleOnly=false,bool AutoRefresh=true,double Zoom=1,double CenterLongitude=0,double CenterLatitude=0)
{
    public ObservatorySettings Validated()=>this with{Version=1,Page="Earthquakes",Preset=Math.Clamp(Preset,0,2),MinimumMagnitude=Clamp(MinimumMagnitude,0,9),Zoom=Clamp(Zoom,1,16),CenterLongitude=Clamp(CenterLongitude,-180,180),CenterLatitude=Clamp(CenterLatitude,-90,90)};
    private static double Clamp(double n,double low,double high)=>double.IsFinite(n)?Math.Clamp(n,low,high):Math.Max(low,0);
}
// Equirectangular map math in logical pixels; display scaling is Avalonia's responsibility.
public sealed class WorldProjection
{
    public double Zoom{get;private set;}=1;
    public double CenterLongitude{get;private set;}
    public double CenterLatitude{get;private set;}
    public double Width{get;private set;}=800;
    public double Height{get;private set;}=400;
    public double Scale=>Math.Max(.001,Math.Min(Width/360,Height/180))*Zoom;
    public void Resize(double width,double height){Width=Math.Max(1,width);Height=Math.Max(1,height);Clamp();}
    public void Set(double zoom,double longitude,double latitude){Zoom=double.IsFinite(zoom)?Math.Clamp(zoom,1,16):1;CenterLongitude=double.IsFinite(longitude)?longitude:0;CenterLatitude=double.IsFinite(latitude)?latitude:0;Clamp();}
    private void Clamp(){double lon=Math.Min(180,Width/(2*Scale)),lat=Math.Min(90,Height/(2*Scale));CenterLongitude=Math.Clamp(CenterLongitude,-180+lon,180-lon);CenterLatitude=Math.Clamp(CenterLatitude,-90+lat,90-lat);}
    public (double X,double Y) Project(double lon,double lat)=>(Width/2+(lon-CenterLongitude)*Scale,Height/2-(lat-CenterLatitude)*Scale);
    public (double Lon,double Lat) Unproject(double x,double y)=>(CenterLongitude+(x-Width/2)/Scale,CenterLatitude-(y-Height/2)/Scale);
    public void Pan(double x,double y){CenterLongitude-=x/Scale;CenterLatitude+=y/Scale;Clamp();}
    public void ZoomAt(double factor,double x,double y){var before=Unproject(x,y);Zoom=Math.Clamp(Zoom*factor,1,16);var after=Unproject(x,y);CenterLongitude+=before.Lon-after.Lon;CenterLatitude+=before.Lat-after.Lat;Clamp();}
    public bool Contains(Earthquake e){var p=Project(e.Longitude,e.Latitude);return p.X>=0&&p.X<=Width&&p.Y>=0&&p.Y<=Height;}
}
