using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BEN_TOOLBOX_2_0.Services;

// Explicit allowlist: no arbitrary commands or user-supplied script paths.
public sealed record ToolDefinition(string Id, string Category, string Name, string Description, string RelativePath)
{
    public string Status => Id == "pixel-art-converter" ? "NATIVE AVALONIA  /  IMAGE PROCESSING" : Id is "content-inspector" or "sprite-inventory" ? "NATIVE AVALONIA  /  READ ONLY" : "WINDOWS POWERSHELL  /  LOCAL";
}

public static class ToolCatalog
{
    private static readonly ToolDefinition[] Tools =
    {
        new("sprite-splitter","Game","Sprite Splitter","Cut PNG sprite sheets into separate images.","GameDev_Toolbox/Tools/SpriteSplitter/SpriteSplitter.ps1"),
        new("pixel-art-converter","Game","Pixel Art Converter","Pixelate PNGs, edit palettes, export or send converted images to Sprite Batch.",""),
        new("sprite-batch","Game","Sprite Batch Tool","Resize and rename PNG assets in batches.","GameDev_Toolbox/Tools/SpriteBatchTool/SpriteBatchTool.ps1"),
        new("resource-checker","Game","Resource Checker","Check resource paths and artwork connections.","GameDev_Toolbox/Tools/ResourceChecker/ResourceChecker.ps1"),
        new("artwork-prompter","Game","Artwork Design Prompter","Compose prompts for game artwork and biomes.","GameDev_Toolbox/Tools/ArtworkDesignPrompter/ArtworkDesignPrompter.ps1"),
        new("folder-backup","Files","Folder Backup","Create dated folder backups with reports.","File_Toolbox/Tools/Backup/Backup.ps1"),
        new("clipboard-png","Files","Clipboard to PNG","Save Windows snips from the clipboard as numbered PNG files.",""),
        new("duplicates","Files","Duplicate File Finder","Compare file names and content; optionally recycle verified matches.","File_Toolbox/Tools/DuplicateFileFinder/DuplicateFileFinder.ps1"),
        new("sprite-inventory","Inspect","Sprite Inventory","Browse Godot sprite images, dimensions, file sizes and lazy previews.",""),
        new("content-inspector","Inspect","Items & Resource Inspector","Browse items, drops, crafting recipes, biome bindings and validation reports.",""),
        new("lighting","Inspect","Lighting & Shadow Inspector","Inspect the main light and shadow configuration.","GameDev_Toolbox/Tools/Inspectors/LightingInspector/LightingInspector.ps1"),
        new("file-size","Inspect","File Size Inspector","Find large project assets and files.","GameDev_Toolbox/Tools/Inspectors/FileSizeInspector/FileSizeInspector.ps1"),
        new("biome","Inspect","Biome Population Inspector","Inspect biome content and species weights.","GameDev_Toolbox/Tools/Inspectors/BiomeContentInspector/BiomePopulationInspector.ps1"),
        new("asset-usage","Inspect","Asset Usage Finder","Find where project assets are referenced.","GameDev_Toolbox/Tools/Inspectors/AssetUsageFinder/AssetUsageFinder.ps1")
    };

    public static IReadOnlyList<ToolDefinition> ByCategory(string category) =>
        Tools.Where(t => t.Category == category).ToArray();

    public static bool IsApproved(ToolDefinition tool) =>
        Tools.Any(t => t.Id == tool.Id && t.RelativePath == tool.RelativePath);
}

public sealed class ToolLauncher
{
    public void Launch(ToolDefinition tool)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("These PowerShell utilities currently require Windows.");
        if (!ToolCatalog.IsApproved(tool))
            throw new InvalidOperationException("Unapproved tool.");
        // Resolve from the running app's directory, not its current working directory.
        var root = FindToolRoot() ?? throw new DirectoryNotFoundException(
            "BEN_TOOLBOX_OLDVERSION was not found. Run from the repository checkout.");
        var script = Path.GetFullPath(Path.Combine(root, tool.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!script.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(script))
            throw new FileNotFoundException("Tool script was not found in the legacy toolbox.", script);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell executable not found.", powershell);
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(script)!,
            CreateNoWindow = false
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-STA");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        if (Process.Start(start) is null)
            throw new InvalidOperationException("Failed to start PowerShell.");
    }

    private static string? FindToolRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; directory != null && i < 8; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "BEN_TOOLBOX_OLDVERSION");
            if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }
}

