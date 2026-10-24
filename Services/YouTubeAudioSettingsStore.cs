using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record YouTubeAudioSettings(string Executable, string FfmpegFolder, string OutputFolder, int Quality);
public static class YouTubeAudioSettingsStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BEN_TOOLBOX_2_0","youtube-audio.json");
    public static async Task<YouTubeAudioSettings?> LoadAsync()
    {
        try
        {
            if(!File.Exists(FilePath)||new FileInfo(FilePath).Length>16000)return null;
            return JsonSerializer.Deserialize<YouTubeAudioSettings>(await File.ReadAllTextAsync(FilePath));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    public static async Task SaveAsync(YouTubeAudioSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(settings));File.Move(temp,FilePath,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
