using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
public sealed class UsgsEarthquakeService:IDisposable
{
    public const int MaximumBytes=4*1024*1024,MaximumEvents=5000;
    private readonly HttpClient client;
    private readonly string directory;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<int,QuakeSnapshot> cache=new();
    private readonly Dictionary<int,DateTimeOffset> retryAfter=new(),cacheUntil=new();
    public UsgsEarthquakeService(string? directory=null,HttpMessageHandler? handler=null)
    {
        this.directory=directory??ObservatorySettingsStore.DirectoryPath;
        client=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BEN-TOOLBOX-InternetObservatory/1.0");client.DefaultRequestHeaders.Accept.ParseAdd("application/geo+json, application/json");
    }
    private static void Validate(int preset){if(preset<0||preset>=QuakeFeed.Presets.Length)throw new ArgumentOutOfRangeException(nameof(preset));}
    public async Task<QuakeResult> LoadCachedAsync(int preset,CancellationToken token)
    {Validate(preset);await gate.WaitAsync(token);try{return new(await LoadCache(preset,token),FromCache:true);}finally{gate.Release();}}
    public async Task<QuakeResult> RefreshAsync(int preset,CancellationToken token)
    {
        Validate(preset);await gate.WaitAsync(token);QuakeSnapshot? previous=null;
        try
        {
            previous=await LoadCache(preset,token);var now=DateTimeOffset.UtcNow;
            if(retryAfter.TryGetValue(preset,out var retry)&&retry>now)return new(previous,$"Provider backoff until {retry:HH:mm:ss} UTC.",true);
            if(previous is not null&&cacheUntil.TryGetValue(preset,out var expiry)&&expiry>now)return new(previous);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request=new HttpRequestMessage(HttpMethod.Get,QuakeFeed.Presets[preset].Uri);
            if(previous?.ETag is {} etag&&EntityTagHeaderValue.TryParse(etag,out var tag))request.Headers.IfNoneMatch.Add(tag);
            if(previous?.LastModified is {} modified)request.Headers.IfModifiedSince=modified;
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false);now=DateTimeOffset.UtcNow;
            var delay=response.Headers.RetryAfter?.Delta??response.Headers.RetryAfter?.Date-now;
            if(delay is {} d&&d>TimeSpan.Zero)retryAfter[preset]=now+Cap(d);
            DateTimeOffset? freshUntil=null;
            if(response.Headers.CacheControl?.MaxAge is {} maxAge&&response.Headers.CacheControl.NoCache!=true)
            {var remaining=maxAge-(response.Headers.Age??TimeSpan.Zero);if(remaining>TimeSpan.Zero)freshUntil=now+Cap(remaining);}
            if(response.StatusCode==HttpStatusCode.NotModified)
            {
                if(previous is null)throw new InvalidDataException("304 received without cached data.");
                var validated=previous with{FetchedUtc=now};await Remember(preset,validated,freshUntil,response.Headers.CacheControl?.NoStore==true,timeout.Token);return new(validated);
            }
            if(!response.IsSuccessStatusCode)
            {
                if((int)response.StatusCode==429||(int)response.StatusCode>=500)retryAfter[preset]=now+Cap(delay is {} backoff&&backoff>TimeSpan.FromMinutes(2)?backoff:TimeSpan.FromMinutes(2));
                throw new HttpRequestException($"USGS HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }
            if(response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/geo+json"))throw new InvalidDataException("USGS did not return JSON.");
            var bytes=await ReadBoundedAsync(response.Content,timeout.Token);
            var parsed=await Task.Run(()=>Parse(bytes,now),timeout.Token);
            var snapshot=parsed with{ETag=response.Headers.ETag?.ToString(),LastModified=response.Content.Headers.LastModified};
            var old=previous?.Events.ToDictionary(e=>e.Id)??new Dictionary<string,Earthquake>();
            int added=previous is null?0:snapshot.Events.Count(e=>!old.ContainsKey(e.Id));int updated=snapshot.Events.Count(e=>old.TryGetValue(e.Id,out var before)&&e.UpdatedUtc>before.UpdatedUtc);
            token.ThrowIfCancellationRequested();await Remember(preset,snapshot,freshUntil,response.Headers.CacheControl?.NoStore==true,timeout.Token);return new(snapshot,NewEvents:added,UpdatedEvents:updated);
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested){return new(previous,"USGS request timed out after 20 seconds.",true);}
        catch(Exception ex) when(ex is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException){return new(previous,ex.Message,true);}
        finally{gate.Release();}
    }
    private static TimeSpan Cap(TimeSpan value)=>value>TimeSpan.FromDays(1)?TimeSpan.FromDays(1):value;
    public static async Task<byte[]> ReadBoundedAsync(HttpContent content,CancellationToken token)
    {
        if(content.Headers.ContentLength>MaximumBytes)throw new InvalidDataException("Feed exceeds the 4 MiB limit.");
        await using var stream=await content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await stream.ReadAsync(buffer,token))>0){if(output.Length+read>MaximumBytes)throw new InvalidDataException("Feed exceeds the 4 MiB limit.");output.Write(buffer,0,read);}return output.ToArray();
    }
    public static QuakeSnapshot Parse(byte[] bytes,DateTimeOffset fetched)
    {
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("Feed too large.");using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=32});var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||Text(root,"type")!="FeatureCollection"||!root.TryGetProperty("features",out var features)||features.ValueKind!=JsonValueKind.Array||features.GetArrayLength()>MaximumEvents)throw new InvalidDataException("Unsupported USGS feed shape or event limit.");
        if(!root.TryGetProperty("metadata",out var metadata)||metadata.ValueKind!=JsonValueKind.Object||Time(metadata,"generated") is not {} generated)throw new InvalidDataException("Provider timestamp missing.");
        var records=new Dictionary<string,Earthquake>();int skipped=0;
        foreach(var f in features.EnumerateArray())
        {
            if(f.ValueKind!=JsonValueKind.Object||!f.TryGetProperty("properties",out var p)||p.ValueKind!=JsonValueKind.Object||!f.TryGetProperty("geometry",out var g)||g.ValueKind!=JsonValueKind.Object||Text(g,"type")!="Point"||!g.TryGetProperty("coordinates",out var c)||c.ValueKind!=JsonValueKind.Array||c.GetArrayLength()<2){skipped++;continue;}
            var id=Text(f,"id");var lon=Number(c[0]);var lat=Number(c[1]);var time=Time(p,"time");
            if(string.IsNullOrWhiteSpace(id)||id.Length>200||lon is null||lat is null||lon< -180||lon>180||lat< -90||lat>90||time is null){skipped++;continue;}
            var mag=p.TryGetProperty("mag",out var m)?Number(m):null;if(mag is < -5 or > 12)mag=null;
            var place=Text(p,"place")??"Location not reported";if(place.Length>500)place=place.Substring(0,500);
            bool? tsunami=p.TryGetProperty("tsunami",out var ts)&&ts.ValueKind==JsonValueKind.Number&&ts.TryGetInt32(out var flag)?flag==1:null;
            var e=new Earthquake(id,mag,place,time.Value,Time(p,"updated")??time.Value,lon.Value,lat.Value,c.GetArrayLength()>2?Number(c[2]):null,tsunami,SafeEventUrl(Text(p,"url")));
            if(!records.TryGetValue(id,out var old)||e.UpdatedUtc>old.UpdatedUtc)records[id]=e;
        }
        return new(records.Values.OrderByDescending(e=>e.TimeUtc).ToArray(),generated,fetched,Skipped:skipped);
    }
    private static string? Text(JsonElement e,string key)=>e.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    private static double? Number(JsonElement e)=>e.ValueKind==JsonValueKind.Number&&e.TryGetDouble(out var n)&&double.IsFinite(n)?n:null;
    private static DateTimeOffset? Time(JsonElement e,string key){if(!e.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetInt64(out var ms))return null;try{return DateTimeOffset.FromUnixTimeMilliseconds(ms);}catch(ArgumentOutOfRangeException){return null;}}
    public static string? SafeEventUrl(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Host=="earthquake.usgs.gov"&&u.IsDefaultPort&&string.IsNullOrEmpty(u.UserInfo)?u.AbsoluteUri:null;
    private string CachePath(int preset)=>Path.Combine(directory,"quakes-"+QuakeFeed.Presets[preset].File+".json");
    private sealed record CacheFile(int Version,int Preset,QuakeSnapshot Snapshot);
    private async Task<QuakeSnapshot?> LoadCache(int preset,CancellationToken token)
    {
        if(cache.TryGetValue(preset,out var existing))return existing;
        try
        {
            var path=CachePath(preset);if(!File.Exists(path)||new FileInfo(path).Length>MaximumBytes)return null;
            var json=await File.ReadAllTextAsync(path,token);var stored=await Task.Run(()=>JsonSerializer.Deserialize<CacheFile>(json),token);
            if(stored is null||stored.Version!=1||stored.Preset!=preset||stored.Snapshot is not {} value||value.Events is null||value.Events.Length>MaximumEvents||DateTimeOffset.UtcNow-value.FetchedUtc>TimeSpan.FromDays(30)||value.FetchedUtc>DateTimeOffset.UtcNow.AddMinutes(5))return null;
            if(value.Events.Any(e=>e is null||string.IsNullOrWhiteSpace(e.Id)||e.Id.Length>200||e.Place is null||e.Place.Length>500||!double.IsFinite(e.Longitude)||!double.IsFinite(e.Latitude)||Math.Abs(e.Longitude)>180||Math.Abs(e.Latitude)>90||e.Magnitude is {} m&&(!double.IsFinite(m)||m< -5||m>12)))return null;
            var safe=value with{Events=value.Events.Select(e=>e with{Url=SafeEventUrl(e.Url)}).GroupBy(e=>e.Id).Select(g=>g.OrderByDescending(e=>e.UpdatedUtc).First()).ToArray()};cache[preset]=safe;return safe;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    private async Task Remember(int preset,QuakeSnapshot snapshot,DateTimeOffset? expiry,bool noStore,CancellationToken token)
    {
        if(expiry is {} until)cacheUntil[preset]=until;else cacheUntil.Remove(preset);
        if(noStore){cache.Remove(preset);cacheUntil.Remove(preset);try{File.Delete(CachePath(preset));}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}return;}
        cache[preset]=snapshot;var temp=CachePath(preset)+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{Directory.CreateDirectory(directory);var bytes=JsonSerializer.SerializeToUtf8Bytes(new CacheFile(1,preset,snapshot));if(bytes.Length>MaximumBytes)return;await File.WriteAllBytesAsync(temp,bytes,token);File.Move(temp,CachePath(preset),true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        finally{if(File.Exists(temp))try{File.Delete(temp);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}}
    }
    public void Dispose()=>client.Dispose();
}
