using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
public static class ObservatorySettingsStore
{
    public static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BEN_TOOLBOX_2_0","InternetObservatory");
    public static async Task<ObservatorySettings> LoadAsync(string? directory=null,CancellationToken token=default)
    {
        try{var path=Path.Combine(directory??DirectoryPath,"settings.json");if(!File.Exists(path)||new FileInfo(path).Length>16000)return new();var value=JsonSerializer.Deserialize<ObservatorySettings>(await File.ReadAllTextAsync(path,token));return value?.Version==1?value.Validated():new();}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public static async Task SaveAsync(ObservatorySettings settings,string? directory=null)
    {
        var path=Path.Combine(directory??DirectoryPath,"settings.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(settings.Validated()));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
