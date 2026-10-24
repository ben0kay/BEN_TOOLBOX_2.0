using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using BEN_TOOLBOX_2_0.Models;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record SeasonalProjectFile(int Version,string SourcePath,string SourceHash,int Width,int Height,
    byte[] Automatic,byte[] Added,byte[] Removed,byte[] Protected,FoliageDetection Detection,SeasonalLook Look,BranchPrototype? Branches=null);
public sealed record SeasonalProjectLoad(SeasonalMaskProject Project,bool SourceChanged);
public static class SeasonalProjectStore
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static void Save(SeasonalMaskProject p,string path,CancellationToken token)
    {
        var data=new SeasonalProjectFile(1,p.SourcePath,p.SourceHash,p.Width,p.Height,p.Automatic,p.Added,p.Removed,p.Protected,p.Detection,p.Look,p.Branches);
        SeasonalMaskService.SaveNew(path,JsonSerializer.SerializeToUtf8Bytes(data,Json),token);
    }
    public static SeasonalProjectLoad Load(string path,CancellationToken token,string? relink=null)
    {
        if(new FileInfo(path).Length>48_000_000)throw new IOException("Project exceeds size limit.");
        token.ThrowIfCancellationRequested();
        var data=JsonSerializer.Deserialize<SeasonalProjectFile>(File.ReadAllText(path))??throw new IOException("Empty project.");
        if(data.Version!=1||data.Width<=0||data.Height<=0||data.Width>4096||data.Height>4096||(long)data.Width*data.Height>SeasonalMaskProject.MaxPixels)throw new IOException("Unsupported project version/dimensions.");
        int count=data.Width*data.Height;
        foreach(var mask in new[]{data.Automatic,data.Added,data.Removed,data.Protected})if(mask is null||mask.Length!=count)throw new IOException("Invalid mask dimensions.");
        if(data.Detection is null||data.Look is null)throw new IOException("Missing settings.");
        SeasonalMaskService.Validate(data.Detection);SeasonalMaskService.Validate(data.Look);
        var source=SeasonalMaskService.Load(relink??data.SourcePath,token);
        if(source.Width!=data.Width||source.Height!=data.Height)throw new IOException("Source dimensions differ. Existing masks cannot be aligned to this image.");
        source.Automatic=data.Automatic;source.Added=data.Added;source.Removed=data.Removed;source.Protected=data.Protected;source.Detection=data.Detection;source.Look=data.Look;source.Branches=data.Branches??new();
        if(source.Branches.Tips?.Length>16)throw new IOException("Too many branch prototype points.");
        return new(source,!source.SourceHash.Equals(data.SourceHash,StringComparison.OrdinalIgnoreCase));
    }
    public static string PresetFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BEN_TOOLBOX_2_0","SeasonalLooks");
    public static void SaveLook(string name,SeasonalLook look,CancellationToken token)
    {
        SeasonalMaskService.Validate(look);
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$"))throw new ArgumentException("Preset name: 1–64 letters/digits/spaces, _ or -.");
        Directory.CreateDirectory(PresetFolder);
        SeasonalMaskService.SaveNew(Path.Combine(PresetFolder,name+".json"),JsonSerializer.SerializeToUtf8Bytes(look,Json),token);
    }
    public static SeasonalLook LoadLook(string path)
    {
        if(new FileInfo(path).Length>8192)throw new IOException("Invalid preset size.");
        var look=JsonSerializer.Deserialize<SeasonalLook>(File.ReadAllText(path))??throw new IOException("Empty preset.");
        SeasonalMaskService.Validate(look);return look;
    }
}
