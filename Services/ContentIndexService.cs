using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static BEN_TOOLBOX_2_0.Models.ContentIndexModels;

namespace BEN_TOOLBOX_2_0.Services;

// Reads derived reports. Never writes game definitions, reports, recipes or catalogs.
public static class ContentIndexService
{
    public const long MaximumBytes = 32 * 1024 * 1024;
    public static async Task<Report> ReadAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Report exceeds the 32 MiB limit.");
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 64 }, token);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("SchemaVersion", out var version) ||
            !version.TryGetInt32(out int schema) || schema != 1)
            throw new InvalidDataException("Expected a schema-v1 ContentIndex.json from the game's Pass 6 exporter.");
        foreach (var name in new[] { "Items", "Sources", "Recipes", "Profiles", "OreSpawns", "Diagnostics" })
            if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Report is missing the " + name + " array. Regenerate it through Godot.");
        var report = root.Deserialize<Report>() ?? throw new InvalidDataException("Empty content report.");
        // Reject null nested rows/arrays rather than breaking later during navigation.
        foreach (var item in report.Items)
            if (item == null || item.Capabilities == null || item.RegistrationPaths == null || item.SourcePaths == null || item.ConsumedBy == null || item.ProducedBy == null)
                throw new InvalidDataException("Malformed item row.");
        foreach (var source in report.Sources)
        {
            if (source == null || source.Rewards == null || source.Biomes == null || source.RegistrationPaths == null)
                throw new InvalidDataException("Malformed source row.");
            if (source.Rewards.Any(x => x == null) || source.Biomes.Any(x => x == null || x.CatalogPaths == null))
                throw new InvalidDataException("Malformed reward or biome row.");
        }
        if (report.Recipes.Any(x => x == null || x.Ingredients == null || x.RegistrationPaths == null || x.Ingredients.Any(i => i == null)) ||
            report.Profiles.Any(x => x == null || x.SourcePaths == null || x.Rewards == null || x.Rewards.Any(i => i == null)) ||
            report.OreSpawns.Any(x => x == null) || report.Diagnostics.Any(x => x == null))
            throw new InvalidDataException("Malformed recipe, profile or diagnostic row.");
        return report;
    }

    public static string? ResolveResource(string projectRoot, string locator)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !File.Exists(Path.Combine(projectRoot, "project.godot")) ||
            string.IsNullOrWhiteSpace(locator) || !locator.StartsWith("res://", StringComparison.Ordinal)) return null;
        string relative = locator.Substring(6).Split(new[] { "::", "#" }, StringSplitOptions.None)[0];
        if (Path.IsPathRooted(relative) || relative.Contains(':')) return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(root + Path.DirectorySeparatorChar, comparison) && File.Exists(path) ? path : null;
    }

    public static string? FindReport(string projectRoot)
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BenTools", "ContentReports", "isometric-new-2d", "ContentIndex.json")
        };
        string project = Path.Combine(projectRoot, "project.godot");
        if (File.Exists(project))
        {
            var nameLine = File.ReadLines(project).FirstOrDefault(x => x.StartsWith("config/name=", StringComparison.Ordinal));
            if (nameLine != null)
            {
                try
                {
                    var name = JsonSerializer.Deserialize<string>(nameLine.Substring("config/name=".Length));
                    if (!string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Godot", "app_userdata", name, "ContentDiscovery", "ContentIndex.json"));
                }
                catch (JsonException) { }
            }
        }
        return candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static Report Demo()
    {
        var report = new Report { Scope = "DEMO — illustrative subset, not your current project's report" };
        const string bark = "res://ITEMS/Definitions/RawMaterials/Organics/Bark/Bark.tres";
        const string fiber = "res://ITEMS/Definitions/RawMaterials/Organics/PlantFiber/PlantFiber.tres";
        const string tree = "res://WORLD/Contents/Vegetation/Trees/CarbonTree.tres";
        const string profile = "res://WORLD/Contents/Vegetation/Trees/CarbonTreeHarvest.tres";
        const string recipe = "res://ITEMS/Definitions/RawMaterials/Organics/PlantFiber/PlantFiberFromBarkRecipe.tres";
        report.Items.Add(new ItemRow { Id = "bark", DisplayName = "Bark", Path = bark, Type = "ItemDefinition",
            Registered = true, MaxStack = 40, WeightKg = .1f, VolumeLitres = .2f,
            RegistrationPaths = new() { "res://ITEMS/Definitions/RawMaterials/Organics/OrganicsCatalog.tres" },
            SourcePaths = new() { tree }, ConsumedBy = new() { recipe } });
        report.Items.Add(new ItemRow { Id = "plant_fiber", DisplayName = "Plant Fiber", Path = fiber, Type = "ItemDefinition",
            Registered = true, MaxStack = 40, WeightKg = .05f, VolumeLitres = .15f, ProducedBy = new() { recipe } });
        report.Sources.Add(new SourceRow { Id = "carbon_tree", Path = tree, Kind = "tree", Work = 18, RequiredStrength = 1,
            Extraction = "Whole source on completed mining work", Rewards = new() {
                new RewardRow { ItemId = "bark", EditPath = profile + "::Drop_1", Field = "Drops[1]", MinimumCount = 1, MaximumCount = 3,
                    Chance = .75, Context = "Independent reward once on source completion" } },
            Biomes = new() { new BindingRow { BiomeId = "carbon_forest", BiomePath = "res://WORLD/Generation/Biomes/Definitions/CarbonForest/CarbonForest.tres",
                Family = "Trees", Weight = 1, BiomeEnabled = true, FamilyEnabled = true, ConfiguredForSelection = true,
                CatalogPaths = new() { "res://WORLD/Generation/Biomes/BiomeCatalog.tres" } } } });
        report.Recipes.Add(new RecipeRow { Id = "plant_fiber_from_bark", Path = recipe, Category = "Materials", Registered = true,
            Description = "Example: strip Bark into Plant Fiber.", OutputItemId = "plant_fiber", OutputPath = fiber, OutputCount = 1, DurationSeconds = 2,
            Ingredients = new() { new IngredientRow { ItemId = "bark", Count = 1, EditPath = recipe + "::Ingredient" } },
            RegistrationPaths = new() { "res://SYSTEMS/Crafting/CraftingCatalog.tres" } });
        report.Profiles.Add(new ProfileRow { Path = profile, SourcePaths = new() { tree }, Rewards = report.Sources[0].Rewards });
        return report;
    }
}
