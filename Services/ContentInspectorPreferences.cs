using System;
using System.IO;
using System.Text.Json;

namespace BEN_TOOLBOX_2_0.Services;

// Only toolbox preferences are saved; no game files or report content are changed.
public sealed class ContentInspectorPreferences
{
    public string ProjectRoot { get; set; } = "";
    public string ReportPath { get; set; } = "";
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BEN_TOOLBOX_2_0", "ContentInspector.json");
    public static ContentInspectorPreferences Load()
    {
        try { return JsonSerializer.Deserialize<ContentInspectorPreferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, FilePath, true);
    }
}
