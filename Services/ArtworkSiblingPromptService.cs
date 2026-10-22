using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BEN_TOOLBOX_2_0.Services;

// This prompt authoring service has no Godot writes or image generation dependencies.
public sealed record ArtworkStylePreset(string Id, string Label, string GameDescription, string ArtDirection)
{
    public override string ToString() => Label;
}

public sealed record ArtworkSiblingVariant(
    string Id, string Label, string[] Categories, string Transformation, string Palette)
{
    public override string ToString() => Label;
}

public sealed record ArtworkSubjectExample(
    string Subject, string Shape, string Material, string Details, string Colours);

public sealed record ArtworkSiblingPromptRequest(
    string StyleId, string Category, string Subject, string Shape,
    string Material, string Details, string Colours, string Notes,
    int Variations, bool Transparent, bool NoCastShadow,
    string? SiblingVariantId, string SiblingNotes,
    bool PreserveStructure, bool PreserveTrunkRoots,
    bool MatchGroundFootprint, bool UseProjectReferences, string ReferenceRepositoryUrl);

public sealed record ArtworkSiblingPromptResult(string MainPrompt, string? SiblingPrompt)
{
    public string Combined => SiblingPrompt is null
        ? "OUTPUT REQUEST: Create exactly 1 image (the main artwork only). Do not create a contact sheet or extra variants."
          + Environment.NewLine + Environment.NewLine + MainPrompt
        : "OUTPUT REQUEST: Create exactly 2 SEPARATE images: image 1 is the healthy/main asset and image 2 is the matching transformed sibling."
          + Environment.NewLine + "Return both as individual images, not a collage, composite, side-by-side picture or sprite sheet. No additional variations or third image."
          + Environment.NewLine + "Keep the shared viewing angle, scale, pixel density and grounded base alignment consistent."
          + Environment.NewLine + Environment.NewLine + "IMAGE 1 OF 2 — MAIN ARTWORK"
          + Environment.NewLine + MainPrompt
          + Environment.NewLine + Environment.NewLine + "IMAGE 2 OF 2 — SIBLING ARTWORK"
          + Environment.NewLine + SiblingPrompt;
}

public static class ArtworkSiblingPromptService
{
    public const string DefaultFantasyRepository = "https://github.com/ben0kay/isometric-fantasy-2d";

    // Actual asset paths verified against the production Fantasy 2D repository (October 2026).
    // Do not invent filenames. Non-tree categories with no committed PNGs use a folder search hint.
    private sealed record ProjectAssetReferences(string SearchRoot, string[] VerifiedPaths);

    private static readonly Dictionary<string, ProjectAssetReferences> ProjectExamples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tree"] = new("WORLD/Content/Vegetation/Trees/",
            new[]
            {
                "WORLD/Content/Vegetation/Trees/Oak/Sprites/tree_01.png",
                "WORLD/Content/Vegetation/Trees/PinkBlossom/Sprites/tree_pink_blossom_01.png",
                "WORLD/Content/Vegetation/Trees/Oak/Sprites/oak_charred.png"
            }),
        ["Shrub"] = new("WORLD/Content/Vegetation/", Array.Empty<string>()),
        ["Plant"] = new("WORLD/Content/Vegetation/", Array.Empty<string>()),
        ["Grass"] = new("WORLD/Content/Vegetation/Grass/Sprites/",
            new[]
            {
                "WORLD/Content/Vegetation/Grass/Sprites/grass_small_01.png",
                "WORLD/Content/Vegetation/Grass/Sprites/grass_small_02.png"
            }),
        ["Rock"] = new("WORLD/Content/Rocks/Sprites/",
            new[] { "WORLD/Content/Rocks/Sprites/rock_01.webp" }),
        ["Ore deposit"] = new("WORLD/Content/Rocks/", Array.Empty<string>()),
        ["Creature"] = new("ENTITIES/", Array.Empty<string>()),
        ["Alien creature"] = new("ENTITIES/", Array.Empty<string>()),
        ["Prop"] = new("WORLD/Content/", Array.Empty<string>()),
        ["Structure"] = new("WORLD/Content/", Array.Empty<string>()),
        ["Surface patch"] = new("WORLD/Content/", Array.Empty<string>()),
        ["Fungus"] = new("WORLD/Content/Vegetation/", Array.Empty<string>())
    };

    public static readonly string[] Categories =
    {
        "Tree", "Shrub", "Plant", "Grass", "Fungus", "Rock", "Surface patch",
        "Ore deposit", "Creature", "Structure", "Prop", "Alien creature"
    };

    public static readonly ArtworkStylePreset[] Styles =
    {
        new("fantasy-topdown", "Fantasy survival / top-down",
            "a 2D top-down fantasy survival sandbox",
            "Semi-realistic, subtly pixel-art-inspired, hand-painted fantasy game sprite; grounded natural materials, legible at gameplay scale, consistent top-down/slightly angled viewing direction."),
        new("scifi-isometric", "Science fiction / isometric",
            "a 2D isometric science-fiction survival game",
            "High-quality hand-painted science-fiction game sprite; believable ecology and materials, legible silhouette, consistent isometric viewing direction.")
    };

    // The recipe registry drives which sibling treatments appear in the window.
    // Charred-tree is intentionally first for oak-tree authoring.
    private static readonly ArtworkSiblingVariant[] Variants =
    {
        new("charred-tree", "Charred / burnt", new[] {"Tree", "Shrub"},
            "Keep the recognizable original trunk, main roots and large branch architecture. Remove all foliage and the full leafy canopy; expose thin, crooked, spindly, partially broken bare branches. Char the bark, split a few outer limbs and add restrained soot and ash damage. No new leaves, active flames, smoke, glowing embers or detached ground debris unless requested.",
            "Charcoal black, ash grey and very dark brown; no healthy green leaves."),
        new("scorched", "Scorched vegetation", new[] {"Plant", "Grass", "Fungus"},
            "Turn living foliage into brittle scorched stems, wilted or missing blades, and burnt organic tips while retaining the rooted shape and recognizable plant type. No active flames or smoke.",
            "Dark umber, charcoal and desaturated ash-grey."),
        new("fire-damaged", "Fire-damaged", new[] {"Structure", "Prop"},
            "Retain the major form and usable silhouette. Add blackened surfaces, soot, heat cracking and selective missing or damaged components; do not turn it into a completely different object. No active fire or smoke.",
            "Soot black, ashy greys and charred material tones."),
        new("dead", "Dead / barren", new[] {"Tree", "Shrub", "Plant", "Grass", "Fungus"},
            "Make the asset naturally dead and dried-out rather than burned. Remove healthy growth and leaves where applicable; show exposed branches, withered stems and brittle tips. Keep the identifying species structure and its rooted position.",
            "Dry greys, faded brown and desaturated tan."),
        new("winter-bare", "Winter / leafless", new[] {"Tree", "Shrub"},
            "Replace the leafy canopy with an anatomically plausible network of bare branches, keeping the same core trunk and roots. This is dormant seasonal growth, not fire damage.",
            "Natural cold brown and muted grey bark."),
        new("autumn", "Autumn foliage", new[] {"Tree", "Shrub", "Plant"},
            "Keep the main canopy and body structure, but change mature foliage into seasonal autumn colour, with a few thin patches or curled leaf edges. Keep the species recognizable.",
            "Muted ochre, rust orange, copper and golden yellow."),
        new("snow", "Snow-covered", new[] {"Tree", "Shrub", "Plant", "Grass", "Fungus", "Rock", "Surface patch", "Structure", "Prop"},
            "Keep the underlying original geometry while adding a modest, physically plausible layer of settled snow to upward-facing surfaces and sheltered ledges. Do not bury or replace the asset.",
            "Original material tones plus subdued white and cold blue-grey snow highlights."),
        new("wet", "Wet / rain-darkened", new[] {"Tree", "Shrub", "Plant", "Grass", "Fungus", "Rock", "Surface patch", "Structure", "Prop"},
            "Preserve the original object and foliage. Make exposed materials subtly damp and slightly darkened, with restrained moisture highlights; avoid exaggerated dripping and added scenery.",
            "Original colours slightly darker and cooler, with small wet highlights."),
        new("corrupted", "Diseased / corrupted", new[] {"*"},
            "Keep the original identity and structural landmarks, then add selective eerie decay, warped details or unnatural growth. Do not obscure the subject or alter the viewpoint.",
            "Desaturated natural colours with restrained sickly or arcane accents."),
        new("stump", "Felled stump", new[] {"Tree"},
            "Keep the species-specific lower trunk, flare and visible root foundation, but remove the crown and upper trunk. Show a convincing cut or splintered broken top. Keep the same ground anchor.",
            "Original bark colours plus exposed timber and weathered end grain."),
        new("sapling", "Young / sapling", new[] {"Tree", "Shrub", "Plant"},
            "Create an earlier-growth version of the same species, using a thinner trunk or stem, smaller crown and fewer branches while maintaining recognizable defining features.",
            "Youthful but natural variations of the base colours."),
        new("mossy", "Moss-covered / overgrown", new[] {"Tree", "Rock", "Structure", "Prop"},
            "Preserve the object form. Add small, believable areas of moss and limited rooted overgrowth along cracks or damp sheltered surfaces; keep details readable.",
            "Natural base materials with subdued green moss."),
        new("cracked", "Cracked / weathered", new[] {"Rock", "Ore deposit", "Structure", "Prop"},
            "Keep the recognizable major mass and outline while introducing physically plausible fractures, erosion and worn edges.",
            "Original material palette, slightly desaturated at damaged edges."),
        new("custom", "Custom sibling (use notes)", new[] {"*"},
            "Apply the change described in the sibling notes, while preserving the asset identity wherever the requested change allows.",
            "Use a variant-appropriate palette derived from the sibling notes.")
    };

    private static readonly Dictionary<string, ArtworkSubjectExample> Examples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tree"] = new("Oak tree", "Broad mature leafy crown; stout trunk with visible roots",
            "Aged brown bark and dense oak leaves", "Natural branching; clear sprite-sized silhouette",
            "Forest greens and earthy bark browns"),
        ["Shrub"] = new("Forest berry bush", "Rounded bushy crown, low rooted stem",
            "Woody twigs and small leaves", "Distinct clusters readable at game scale",
            "Muted green leaves and dark berries"),
        ["Plant"] = new("Wild fern", "Natural spreading fern fronds",
            "Fine organic leaf structure", "Readable leaf grouping at sprite size",
            "Deep forest green"),
        ["Grass"] = new("Meadow grass tuft", "Irregular rooted clump of upright blades",
            "Soft dense grass", "Recognizable outline without tiny noise", "Several natural greens"),
        ["Fungus"] = new("Woodland mushroom", "Short stem with broad rounded cap",
            "Soft organic cap with textured stalk", "Clear shape from top-down angle",
            "Warm brown cap and pale stem"),
        ["Rock"] = new("Forest boulder", "Irregular weighty boulder with flat footprint",
            "Weathered stone", "Readable facets and subtle erosion",
            "Cool greys with earthy accents"),
        ["Surface patch"] = new("Forest leaf litter", "Small irregular ground patch",
            "Dry leaves and natural debris", "Tile-friendly outline without hard border",
            "Earth brown and faded green"),
        ["Ore deposit"] = new("Iron ore rock", "Rough mineral-bearing stone",
            "Stone with embedded mineral seams", "Ore visible at small scale",
            "Grey stone and muted iron red"),
        ["Creature"] = new("Forest wolf", "Recognizable quadruped profile from above",
            "Natural fur and paws", "Clear anatomy, no scenery",
            "Natural grey-brown fur"),
        ["Structure"] = new("Timber watchtower", "Compact wooden tower with grounded support",
            "Weathered timber", "Distinct readable silhouette and structural joints",
            "Warm brown wood and iron accents"),
        ["Prop"] = new("Wooden supply crate", "Simple square crate with defined top",
            "Aged planks and metal brackets", "Readable construction and corners",
            "Wood browns and muted metal"),
        ["Alien creature"] = new("Alien beetle", "Broad segmented body with visible legs",
            "Chitin shell", "Clear game-scale silhouette", "Muted cyan and indigo")
    };

    public static ArtworkSubjectExample ExampleFor(string category) =>
        Examples.TryGetValue(category, out var example) ? example : Examples["Tree"];

    public static IReadOnlyList<ArtworkSiblingVariant> VariantsFor(string category) =>
        Variants.Where(p => p.Categories.Contains("*") ||
            p.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)).ToArray();

    public static ArtworkSiblingPromptResult Compose(ArtworkSiblingPromptRequest request)
    {
        if (request.Variations is < 1 or > 12)
            throw new ArgumentException("Variation count must be between 1 and 12.");
        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ArgumentException("Enter a subject before generating artwork prompts.");

        var style = Styles.FirstOrDefault(s => s.Id == request.StyleId) ?? Styles[0];
        var main = new StringBuilder();
        main.AppendLine("Create " + request.Variations + " cohesive original artwork variation(s) for " + style.GameDescription + ".");
        main.AppendLine("Asset category: " + request.Category + ". Subject: " + request.Subject.Trim() + ".");
        AppendOptional(main, "Shape", request.Shape);
        AppendOptional(main, "Material", request.Material);
        AppendOptional(main, "Details", request.Details);
        AppendOptional(main, "Colours", request.Colours);
        AppendPresentation(main, style, request.Transparent, request.NoCastShadow);
        if (request.UseProjectReferences)
            AppendProjectReferences(main, request);
        AppendOptional(main, "Additional requirements", request.Notes);

        if (string.IsNullOrWhiteSpace(request.SiblingVariantId))
            return new ArtworkSiblingPromptResult(main.ToString().TrimEnd(), null);

        var preset = VariantsFor(request.Category).FirstOrDefault(p => p.Id == request.SiblingVariantId);
        if (preset is null)
            throw new ArgumentException("The selected sibling preset is not available for this category.");

        var sibling = new StringBuilder();
        sibling.AppendLine("Create " + request.Variations + " matching sibling variation(s) of the SAME asset for " + style.GameDescription + ".");
        sibling.AppendLine("Original asset: " + request.Subject.Trim() + " (category: " + request.Category + ").");
        sibling.AppendLine("Sibling treatment: " + preset.Label + ".");
        sibling.AppendLine("Original design references (for identity and continuity, not mandatory healthy features):");
        AppendOptional(sibling, "Original shape", request.Shape);
        AppendOptional(sibling, "Original material", request.Material);
        AppendOptional(sibling, "Original details", request.Details);
        AppendOptional(sibling, "Original colours", request.Colours);
        sibling.AppendLine("Required transformation: " + preset.Transformation);
        sibling.AppendLine("Variant colours: " + preset.Palette);
        sibling.AppendLine("Where the variant conflicts with healthy foliage, original colours or undamaged material, the transformation MUST override those original features.");
        if (request.PreserveStructure)
            sibling.AppendLine("Continuity: retain recognizable species/object identity, large structural landmarks, approximate size and the same sprite placement; allow the canopy or damaged outer silhouette to change as required.");
        if (request.PreserveTrunkRoots && (request.Category is "Tree" or "Shrub"))
            sibling.AppendLine("Keep the same main trunk and grounded root foundation intact, with the same base/anchor. Only the outer branches, canopy and surface treatment should substantially change unless explicitly requested.");
        if (request.MatchGroundFootprint)
            sibling.AppendLine("STRICT BASE MATCH: Preserve the EXACT same ground-contact foundation outline, lower trunk/root footprint, placement anchor and base shape as image 1. Do not shift, shrink, rotate or redraw the ground foundation differently. Only the canopy, upper branches and allowed material damage should change.");
        sibling.AppendLine("Match the main artwork's camera angle, asset scale, composition, texture treatment and visual style. If the main image is supplied, use it as the visual reference; otherwise follow the original description.");
        AppendPresentation(sibling, style, request.Transparent, request.NoCastShadow);
        if (request.UseProjectReferences)
            AppendProjectReferences(sibling, request);
        if (!string.IsNullOrWhiteSpace(request.Notes))
            sibling.AppendLine("Shared art direction (where compatible with variant): " + request.Notes.Trim());
        AppendOptional(sibling, "Additional sibling requirements", request.SiblingNotes);
        return new ArtworkSiblingPromptResult(main.ToString().TrimEnd(), sibling.ToString().TrimEnd());
    }

    private static void AppendProjectReferences(StringBuilder builder, ArtworkSiblingPromptRequest request)
    {
        var repo = request.ReferenceRepositoryUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(repo, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Segments.Length < 3)
            throw new ArgumentException("Enter a valid https://github.com/owner/repository URL for the artwork references.");

        builder.AppendLine("PROJECT ARTWORK REFERENCES — IMPORTANT: Before generating, IF repository browsing is available, open the project README and examine existing artwork for the SAME category. Match its illustrated style, visual density, sprite scale, outline treatment, palette discipline and top-down/angled viewpoint rather than inventing a new art direction.");
        builder.AppendLine("Project README: " + repo + "/blob/main/README.md");
        if (repo.Equals(DefaultFantasyRepository, StringComparison.OrdinalIgnoreCase))
        {
            var references = ProjectExamples.TryGetValue(request.Category, out var found)
                ? found : new ProjectAssetReferences("WORLD/Content/", Array.Empty<string>());
            builder.AppendLine("Find related " + request.Category.ToLowerInvariant() + " sprites under: " + repo + "/tree/main/" + references.SearchRoot);
            foreach (var path in references.VerifiedPaths)
                builder.AppendLine("Existing example asset: " + repo + "/blob/main/" + path);
            if (references.VerifiedPaths.Length == 0)
                builder.AppendLine("No category-specific image path has been verified in this repository. Search relevant content/actor folders for PNG or WebP examples; do not assume a matching sprite already exists.");
        }
        else
            builder.AppendLine("Search this repository for existing " + request.Category + " artwork PNG/WebP sprites, prioritizing actual game content assets over placeholder/debug files.");

        builder.AppendLine("These are references to inspect, NOT proof they have been viewed. If this image-generation session cannot open the links, ask for reference PNGs to be attached for an exact style match; otherwise use the written style description as a fallback and do not claim to have reviewed the images.");
    }

    private static void AppendOptional(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.AppendLine(label + ": " + value.Trim() + ".");
    }

    private static void AppendPresentation(StringBuilder builder, ArtworkStylePreset style, bool transparent, bool noCastShadow)
    {
        builder.AppendLine("Art direction: " + style.ArtDirection);
        builder.AppendLine(transparent
            ? "Isolated game asset on a genuinely transparent background (alpha)."
            : "Use a simple contrasting background suitable for easy extraction.");
        if (noCastShadow)
            builder.AppendLine("No cast/contact shadow and no ground plane; retain natural internal surface shading.");
        builder.AppendLine("No text, watermark, border, environmental scenery, or presentation frame.");
    }
}
