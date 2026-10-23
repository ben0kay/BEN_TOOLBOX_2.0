using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record PixelPalettePreset(string Name, string[] Colours);
public static class PixelPalettePresets
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEN_TOOLBOX_2_0", "PixelPalettes");
    public static string[] Validate(string[] colours)
    {
        if (colours is null || colours.Length is < 1 or > 256) throw new ArgumentException("A palette needs 1–256 colours.");
        return colours.Select(c => c is not null && Regex.IsMatch(c, "^#?[0-9a-fA-F]{6}$")
            ? "#" + c.TrimStart('#').ToUpperInvariant() : throw new ArgumentException("Use six-digit RGB hex colours, e.g. #63C74D.")).ToArray();
    }
    public static List<PixelPalettePreset> Load(out string warning)
    {
        var presets = new List<PixelPalettePreset>(); warning = "";
        try
        {
            if (!Directory.Exists(Folder)) return presets;
            foreach (var path in Directory.EnumerateFiles(Folder, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<PixelPalettePreset>(File.ReadAllText(path)) ?? throw new IOException("Empty palette.");
                    if (string.IsNullOrWhiteSpace(item.Name)) throw new IOException("Unnamed palette.");
                    if (presets.Any(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))) throw new IOException("Duplicate name.");
                    presets.Add(item with { Colours = Validate(item.Colours) });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { warning = "Some saved palettes could not be loaded: " + ex.Message; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warning = "Palette storage unavailable: " + ex.Message; }
        return presets;
    }
    public static void SaveNew(string name, string[] colours)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Enter a palette name (1–80 characters).");
        var preset = new PixelPalettePreset(name, Validate(colours));
        var existing = Load(out var warning);
        if (warning.Length > 0) throw new IOException(warning);
        if (existing.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new IOException("That name already exists. Choose a new name to save a revised copy.");
        Directory.CreateDirectory(Folder);
        var temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, Path.ChangeExtension(temporary, ".json"));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
