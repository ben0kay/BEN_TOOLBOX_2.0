using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
void Check(bool b,string message){if(!b)throw new Exception(message);}
async Task Throws<T>(Func<Task> action) where T:Exception{try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
string Feature(string id="a",string magnitude="4.2",string geometry="[120,45,12]",long updated=1700000001000)=>"""
{"id":"ID","properties":{"mag":MAG,"place":"Fixture location","time":1700000000000,"updated":UPDATED,"tsunami":null,"url":"https://earthquake.usgs.gov/earthquakes/eventpage/a"},"geometry":{"type":"Point","coordinates":COORDS}}
""".Replace("ID",id).Replace("MAG",magnitude).Replace("COORDS",geometry).Replace("UPDATED",updated.ToString());
string Feed(params string[] features)=>"{\"type\":\"FeatureCollection\",\"metadata\":{\"generated\":1700000010000},\"features\":["+string.Join(',',features)+"]}";
HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};
if(args is ["--online"])
{
    var directory=Path.Combine(Path.GetTempPath(),"usgs-live-"+Guid.NewGuid());
    try{using var service=new UsgsEarthquakeService(directory);var actual=await service.RefreshAsync(0,default);Check(actual.Error is null,actual.Error??"Live feed failed");Console.WriteLine($"Actual HTTP service: {actual.Snapshot!.Events.Length} events, generated {actual.Snapshot.ProviderUtc:O}.");}
    finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
var parsed=UsgsEarthquakeService.Parse(Encoding.UTF8.GetBytes(Feed(Feature(),Feature(updated:1700000002000),Feature("b","null"),Feature("bad","3","null"))),DateTimeOffset.UtcNow);
Check(parsed.Events.Length==2&&parsed.Skipped==1,"Null coordinates/dedup");var a=parsed.Events.Single(e=>e.Id=="a");Check(a.Longitude==120&&a.Latitude==45&&a.DepthKm==12&&a.TimeUtc.ToUnixTimeMilliseconds()==1700000000000&&a.UpdatedUtc.ToUnixTimeMilliseconds()==1700000002000,"Coordinate/time order");Check(parsed.Events.Single(e=>e.Id=="b").Magnitude is null,"Unknown magnitude became zero");
Check(UsgsEarthquakeService.Parse(Encoding.UTF8.GetBytes(Feed()),DateTimeOffset.UtcNow).Events.Length==0,"Empty feed");
await Throws<InvalidDataException>(()=>Task.FromResult(UsgsEarthquakeService.Parse(Encoding.UTF8.GetBytes("{}"),DateTimeOffset.UtcNow)));
await Throws<JsonException>(()=>Task.FromResult(UsgsEarthquakeService.Parse(Encoding.UTF8.GetBytes("bad"),DateTimeOffset.UtcNow)));
var root=Path.Combine(Path.GetTempPath(),"observatory-"+Guid.NewGuid());Directory.CreateDirectory(root);
try
{
    int calls=0;bool etag=false;using(var service=new UsgsEarthquakeService(root,new Handler((request,ct)=>{calls++;etag|=request.Headers.IfNoneMatch.Any();var r=calls==1?Json(Feed(Feature())):new HttpResponseMessage(HttpStatusCode.NotModified);r.Headers.ETag=new EntityTagHeaderValue("\"fixture\"");return Task.FromResult(r);})))
    {Check((await service.RefreshAsync(0,default)).Snapshot?.Events.Length==1,"Successful fetch");var second=await service.RefreshAsync(0,default);Check(second.Error is null&&etag&&second.Snapshot?.Events.Length==1,"304/ETag");}
    using(var service=new UsgsEarthquakeService(root,new Handler((_,_)=>throw new HttpRequestException("offline"))))
    {Check((await service.LoadCachedAsync(0,default)).Snapshot?.Events.Length==1,"Cache reload");var failed=await service.RefreshAsync(0,default);Check(failed.Error is not null&&failed.Snapshot?.Events.Length==1&&failed.IsStale(DateTimeOffset.UtcNow),"Stale cache lost");}
    foreach(var content in new[]{new StringContent("html",Encoding.UTF8,"text/html"),new StringContent("bad",Encoding.UTF8,"application/json"),new StringContent(new string('x',UsgsEarthquakeService.MaximumBytes+1),Encoding.UTF8,"application/json")})
    {using var service=new UsgsEarthquakeService(root,new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content})));var failed=await service.RefreshAsync(0,default);Check(failed.Error is not null&&failed.Snapshot?.Events.Length==1,"Bad response discarded cache");}
    int throttled=0;using(var service=new UsgsEarthquakeService(root,new Handler((_,_)=>{throttled++;var r=new HttpResponseMessage((HttpStatusCode)429);r.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));return Task.FromResult(r);})))
    {await service.RefreshAsync(0,default);await service.RefreshAsync(0,default);Check(throttled==1,"Retry-After ignored");}
    int cached=0;using(var service=new UsgsEarthquakeService(root,new Handler((_,_)=>{cached++;var r=Json(Feed(Feature()));r.Headers.CacheControl=new CacheControlHeaderValue{MaxAge=TimeSpan.FromMinutes(5)};return Task.FromResult(r);})))
    {await service.RefreshAsync(0,default);await service.RefreshAsync(0,default);Check(cached==1,"max-age ignored");}
    using(var service=new UsgsEarthquakeService(root,new Handler((_,_)=>throw new TaskCanceledException("fixture timeout"))))Check((await service.RefreshAsync(0,default)).Error!.Contains("timed out"),"Timeout state");
    using(var cancel=new CancellationTokenSource())using(var service=new UsgsEarthquakeService(root,new Handler(async(_,ct)=>{await Task.Delay(10000,ct);return Json(Feed());})))
    {var pending=service.RefreshAsync(0,cancel.Token);cancel.Cancel();await Throws<OperationCanceledException>(()=>pending);}
    await ObservatorySettingsStore.SaveAsync(new(Preset:2,MinimumMagnitude:4,Zoom:4,CenterLongitude:100,CenterLatitude:30),root);var settings=await ObservatorySettingsStore.LoadAsync(root);Check(settings.Preset==2&&settings.Zoom==4&&settings.CenterLongitude==100,"Settings roundtrip");await File.WriteAllTextAsync(Path.Combine(root,"settings.json"),"corrupt");Check((await ObservatorySettingsStore.LoadAsync(root)).Preset==0,"Corrupt preferences");
    var projection=new WorldProjection();projection.Resize(1000,500);Check(projection.Project(-180,90)==(0,0)&&projection.Project(180,-90)==(1000,500),"World edges");var anchor=projection.Unproject(600,200);projection.ZoomAt(3,600,200);var after=projection.Unproject(600,200);Check(Math.Abs(anchor.Lon-after.Lon)<1e-8&&Math.Abs(anchor.Lat-after.Lat)<1e-8,"Zoom anchor");projection.Pan(20,-30);var point=projection.Project(179,85);var inverse=projection.Unproject(point.X,point.Y);Check(Math.Abs(inverse.Lon-179)<1e-8&&Math.Abs(inverse.Lat-85)<1e-8,"Map inverse");projection.Resize(400,800);projection.Set(double.NaN,double.PositiveInfinity,double.NaN);Check(double.IsFinite(projection.Scale),"Invalid saved view");
    int noStore=0;var noStoreRoot=Path.Combine(root,"no-store");using(var service=new UsgsEarthquakeService(noStoreRoot,new Handler((_,_)=>{noStore++;var r=Json(Feed(Feature()));r.Headers.CacheControl=new CacheControlHeaderValue{NoStore=true,MaxAge=TimeSpan.FromMinutes(5)};return Task.FromResult(r);})))
    {await service.RefreshAsync(0,default);await service.RefreshAsync(0,default);Check(noStore==2&&!File.Exists(Path.Combine(noStoreRoot,"quakes-2.5_day.json")),"No-store ignored");}
    Console.WriteLine("PASS: schema/nulls/dedup/UTC, HTTP/ETag/304, bounded content, stale cache, cache policies/backoff, timeout/cancel, settings and projection/zoom/pan.");
}
finally{Directory.Delete(root,true);}
sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
