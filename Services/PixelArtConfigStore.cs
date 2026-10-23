using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BEN_TOOLBOX_2_0.Services;

// A named snapshot of all image-processing parameters; never includes paths,
// input queues, export filenames, or destructive overwrite options.
public sealed record PixelArtNamedConfig(string Name, PixelArtOptions Options);

public static class PixelArtConfigStore
{
    private const int Version = 1;
    private const int Limit = 100;
    private sealed record ConfigFile(int Version, List<PixelArtNamedConfig> Configs);
    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BEN_TOOLBOX_2_0", "PixelArtConfigs");
    public static string FilePath => Path.Combine(Folder, "master-configs.json");

    public static List<PixelArtNamedConfig> Load()
    {
        if (!File.Exists(FilePath)) return new();
        if (new FileInfo(FilePath).Length > 1024 * 1024)
            throw new IOException("Saved config file is unexpectedly large.");

        var file = JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(FilePath))
                   ?? throw new IOException("Saved config file is empty.");
        if (file.Version != Version || file.Configs is null || file.Configs.Count > Limit)
            throw new IOException("Unsupported or invalid master config file.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configs = new List<PixelArtNamedConfig>();
        foreach (var item in file.Configs)
        {
            if (item is null || item.Options is null)
                throw new IOException("A saved config has missing settings.");
            string name = ValidateName(item.Name);
            if (!names.Add(name))
                throw new IOException("Duplicate saved config names.");
            configs.Add(new PixelArtNamedConfig(name, ValidateOptions(item.Options)));
        }
        return configs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void SaveAs(string name, PixelArtOptions options)
    {
        name = ValidateName(name);
        var all = Load();
        if (all.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("That config name already exists. Choose a different name, or use Update Selected.");
        if (all.Count >= Limit) throw new IOException("Maximum 100 saved configs.");
        all.Add(new PixelArtNamedConfig(name, ValidateOptions(options)));
        Write(all);
    }

    public static void Update(string name, PixelArtOptions options)
    {
        name = ValidateName(name);
        var all = Load();
        int index = all.FindIndex(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new IOException("That saved config no longer exists.");
        all[index] = new PixelArtNamedConfig(all[index].Name, ValidateOptions(options));
        Write(all);
    }

    public static void Delete(string name)
    {
        name = ValidateName(name);
        var all = Load();
        if (all.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0)
            throw new IOException("That saved config no longer exists.");
        Write(all);
    }

    private static string ValidateName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a config name (1–80 characters, no control characters).");
        return name;
    }

    private static PixelArtOptions ValidateOptions(PixelArtOptions o)
    {
        if (o.LogicalWidth is < 16 or > 512 || !Enum.IsDefined(o.Palette) ||
            !Enum.IsDefined(o.Outline) || o.OutlineWidth is < 1 or > 4 ||
            o.AlphaCutoff is < 0 or > 255 || o.RemoveIslands is < 0 or > 16)
            throw new ArgumentException("Saved configuration has invalid grid or edge settings.");
        static bool Valid(double x, double min, double max) =>
            double.IsFinite(x) && x >= min && x <= max;
        if (!Valid(o.Brightness, 0, 2) || !Valid(o.Contrast, 0, 2) ||
            !Valid(o.Saturation, 0, 2) || !Valid(o.Dither, 0, 1) ||
            !Valid(o.SamplingBlend ?? (o.NearestSource ? 1 : 0), 0, 1) ||
            !Valid(o.Hue, -180, 180) || !Valid(o.OutlineOpacity, 0, 1))
            throw new ArgumentException("Saved configuration has invalid numeric settings.");

        string colour = PixelPalettePresets.Validate(new[] { o.OutlineColour })[0];
        var colours = o.CustomColours is { Length: > 0 }
            ? PixelPalettePresets.Validate(o.CustomColours) : null;
        if (o.Palette == PixelPalette.Custom && colours is null)
            throw new ArgumentException("Custom palettes must include their saved colours.");
        return o with { OutlineColour = colour, CustomColours = colours };
    }

    private static void Write(List<PixelArtNamedConfig> configs)
    {
        Directory.CreateDirectory(Folder);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new ConfigFile(Version, configs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
