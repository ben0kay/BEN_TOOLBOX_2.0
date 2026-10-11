using System;
using System.Collections.Generic;
using System.Linq;

namespace BEN_TOOLBOX_2_0.Models;

// Schema v1 mirrors the Godot content export. It contains data only.
public static class ContentIndexModels
{
    public sealed class Report
    {
        public int SchemaVersion { get; set; } = 1;
        public string Scope { get; set; } = "Resource content; not a live campaign or guaranteed spawn report";
        public string[] ScanRoots { get; set; } = Array.Empty<string>();
        public bool Valid => Diagnostics.All(d => d.Severity != "error");
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public List<ItemRow> Items { get; set; } = new();
        public List<SourceRow> Sources { get; set; } = new();
        public List<RecipeRow> Recipes { get; set; } = new();
        public List<ProfileRow> Profiles { get; set; } = new();
        public List<OreSpawnRow> OreSpawns { get; set; } = new();
    }
    public sealed class Diagnostic
    {
        public string Severity { get; set; } = "";
        public string Path { get; set; } = "";
        public string Field { get; set; } = "";
        public string Message { get; set; } = "";
    }
    public sealed class ItemRow
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Type { get; set; } = "";
        public int MaxStack { get; set; }
        public float WeightKg { get; set; }
        public float VolumeLitres { get; set; }
        public List<string> Capabilities { get; set; } = new();
        public bool Registered { get; set; }
        public List<string> RegistrationPaths { get; set; } = new();
        public List<string> SourcePaths { get; set; } = new();
        public List<string> ConsumedBy { get; set; } = new();
        public List<string> ProducedBy { get; set; } = new();
    }
    public sealed class RewardRow
    {
        public string ItemId { get; set; } = "";
        public string EditPath { get; set; } = "";
        public string Field { get; set; } = "";
        public string Context { get; set; } = "";
        public int MinimumCount { get; set; }
        public int MaximumCount { get; set; }
        public double Chance { get; set; }
    }
    public sealed class BindingRow
    {
        public string BiomeId { get; set; } = "";
        public string BiomePath { get; set; } = "";
        public List<string> CatalogPaths { get; set; } = new();
        public string EditPath { get; set; } = "";
        public string Family { get; set; } = "";
        public double Weight { get; set; }
        public bool BiomeEnabled { get; set; }
        public bool FamilyEnabled { get; set; }
        public bool ConfiguredForSelection { get; set; }
    }
    public sealed class SourceRow
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Extraction { get; set; } = "";
        public double Work { get; set; }
        public int RequiredStrength { get; set; }
        public int TotalUnits { get; set; }
        public int UnitsPerBatch { get; set; }
        public List<string> RegistrationPaths { get; set; } = new();
        public List<RewardRow> Rewards { get; set; } = new();
        public List<BindingRow> Biomes { get; set; } = new();
    }
    public sealed class IngredientRow
    {
        public string ItemId { get; set; } = "";
        public int Count { get; set; }
        public string EditPath { get; set; } = "";
    }
    public sealed class RecipeRow
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string OutputItemId { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public int OutputCount { get; set; }
        public double DurationSeconds { get; set; }
        public bool Registered { get; set; }
        public List<string> RegistrationPaths { get; set; } = new();
        public List<IngredientRow> Ingredients { get; set; } = new();
    }
    public sealed class ProfileRow
    {
        public string Path { get; set; } = "";
        public List<string> SourcePaths { get; set; } = new();
        public bool Shared => SourcePaths.Distinct(StringComparer.Ordinal).Count() > 1;
        public List<RewardRow> Rewards { get; set; } = new();
    }
    public sealed class OreSpawnRow
    {
        public string Path { get; set; } = "";
        public string DefinitionPath { get; set; } = "";
        public bool Enabled { get; set; }
        public double ChancePerChunk { get; set; }
        public int PlacementAttempts { get; set; }
        public string Scope { get; set; } = "Surface settings definition; actual campaign assignment and terrain acceptance are not inferred";
    }

}
