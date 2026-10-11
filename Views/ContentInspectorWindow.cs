using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;
using static BEN_TOOLBOX_2_0.Models.ContentIndexModels;

namespace BEN_TOOLBOX_2_0.Views;

// Browsing only. Future editing can sit beside this view without changing its reader.
public sealed class ContentInspectorWindow : Window
{
    private sealed record Entry(string Kind, string Title, string Subtitle, string Path, object Value)
    {
        public string Search => Title + " " + Subtitle + " " + Path;
    }
    private readonly TextBox project = new() { Watermark = "Godot project folder (for locating source files)" };
    private readonly TextBox reportPath = new() { Watermark = "Load the game's ContentIndex.json report" };
    private readonly TextBox search = new() { Watermark = "Search names, IDs, resource paths…" };
    private readonly ComboBox filter = new() { MinWidth = 165 };
    private readonly ListBox browser = new();
    private readonly StackPanel detail = new() { Spacing = 12, Margin = new Thickness(22) };
    private readonly TextBlock summary = Label("", 15, "#86E5F6");
    private readonly TextBlock notice = Label("", 13, "#F0C76A");
    private readonly TextBlock status = Label("Ready", 12, "#9BB7CF");
    private readonly Button refresh = new() { Content = "REFRESH REPORT" };
    private readonly Dictionary<string, Button> navigation = new();
    private readonly ContentInspectorPreferences preferences = ContentInspectorPreferences.Load();
    private Report current = new();
    private string section = "Items";
    private bool demo = true;
    private bool updating;
    private CancellationTokenSource? running;

    public ContentInspectorWindow()
    {
        Title = "Item & Resource Inspector / BEN Toolbox";
        Width = 1390; Height = 900; MinWidth = 1000; MinHeight = 650;
        Background = Brush.Parse("#091522"); Foreground = Brush.Parse("#E8F4FF");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20), RowSpacing = 14 };
        var header = new StackPanel { Spacing = 5 };
        header.Children.Add(Label("CONTENT EXPLORER  /  READ ONLY", 12, "#86E5F6"));
        header.Children.Add(Label("Items & Resources", 29));
        header.Children.Add(Label("Follow an item from its drop sources to its recipes. Inspect exact resources and biome selection bindings.", 14, "#9BB7CF"));
        layout.Children.Add(header);
        var controls = new StackPanel { Spacing = 9 };
        controls.Children.Add(PathRow(project, "PROJECT", BrowseProject));
        controls.Children.Add(PathRow(reportPath, "LOAD JSON", BrowseReport));
        var actions = new WrapPanel { ItemSpacing = 10, LineSpacing = 8 };
        actions.Children.Add(refresh);
        actions.Children.Add(ActionButton("FIND REPORT", async () => {
            try {
                var found = ContentIndexService.FindReport(project.Text?.Trim().Trim('"') ?? "");
                if (found == null) { status.Text = "No report found. Run ContentReportExport.tscn with F6 in your game, then LOAD JSON."; return; }
                reportPath.Text = found; await LoadReport();
            } catch (Exception ex) { status.Text = ex.Message; }
        }));
        actions.Children.Add(ActionButton("REPORT FOLDER", () => { LocateReport(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("DEMO PREVIEW", () => { ShowDemo(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("HOW TO EXPORT", () => { Instructions(); return Task.CompletedTask; }));
        controls.Children.Add(actions); controls.Children.Add(summary); controls.Children.Add(notice);
        Grid.SetRow(controls, 1); layout.Children.Add(controls);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("360,*"), ColumnSpacing = 16 };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 10 };
        var tabs = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var name in new[] { "Items", "Sources", "Recipes", "Profiles", "Diagnostics" })
        {
            var button = new Button { Content = name, Padding = new Thickness(10, 7) };
            button.Click += (_, _) => SetSection(name);
            navigation[name] = button; tabs.Children.Add(button);
        }
        left.Children.Add(tabs);
        var find = new StackPanel { Spacing = 8 }; find.Children.Add(search); find.Children.Add(filter);
        Grid.SetRow(find, 1); left.Children.Add(find);
        browser.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) => {
            if (entry == null) return new TextBlock();
            var card = new StackPanel { Spacing = 5, Margin = new Thickness(9) };
            card.Children.Add(Label(entry.Title, 16));
            card.Children.Add(Label(entry.Subtitle, 12, "#9BB7CF"));
            return card;
        }, true);
        Grid.SetRow(browser, 2); left.Children.Add(browser);
        body.Children.Add(new Border { Background = Brush.Parse("#101F30"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = left });
        var right = new Border { Background = Brush.Parse("#101F30"), CornerRadius = new CornerRadius(10), Child = new ScrollViewer { Content = detail } };
        Grid.SetColumn(right, 1); body.Children.Add(right);
        Grid.SetRow(body, 2); layout.Children.Add(body);
        Grid.SetRow(status, 3); layout.Children.Add(status); Content = layout;
        refresh.Click += async (_, _) => await LoadReport();
        search.TextChanged += (_, _) => { if (!updating) Filter(); };
        filter.SelectionChanged += (_, _) => { if (!updating) Filter(); };
        browser.SelectionChanged += (_, _) => { if (browser.SelectedItem is Entry entry) Show(entry); };
        Closed += (_, _) => running?.Cancel();
        project.Text = preferences.ProjectRoot; reportPath.Text = preferences.ReportPath;
        ShowDemo();
        Opened += async (_, _) => { if (File.Exists(reportPath.Text)) await LoadReport(); };
    }

    private static TextBlock Label(string? text, double size = 14, string color = "#E8F4FF") => new()
    { Text = text ?? "", FontSize = size, Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => await action(); return button;
    }
    private static Grid PathRow(TextBox field, string text, Func<Task> action)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,130"), ColumnSpacing = 10 };
        grid.Children.Add(field); var button = ActionButton(text, action); Grid.SetColumn(button, 1); grid.Children.Add(button); return grid;
    }
    private async Task BrowseProject()
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose your Godot project", AllowMultiple = false });
        if (selected.Count == 0 || !selected[0].Path.IsFile) return;
        var path = selected[0].Path.LocalPath;
        if (!File.Exists(Path.Combine(path, "project.godot"))) { status.Text = "Choose a folder containing project.godot."; return; }
        project.Text = path; SavePreferences();
        if (browser.SelectedItem is Entry entry) Show(entry);
        status.Text = "Project selected. FIND REPORT looks in the standard exporter folders; LOAD JSON lets you choose any report.";
    }
    private async Task BrowseReport()
    {
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load ContentIndex.json", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON content report") { Patterns = new[] { "*.json" } } } });
        if (selected.Count == 0 || !selected[0].Path.IsFile) return;
        reportPath.Text = selected[0].Path.LocalPath; await LoadReport();
    }
    private async Task LoadReport()
    {
        if (running != null) return;
        string path = reportPath.Text?.Trim().Trim('"') ?? "";
        if (!File.Exists(path)) { status.Text = "Choose ContentIndex.json using LOAD JSON."; return; }
        var cancellation = new CancellationTokenSource(); running = cancellation; refresh.IsEnabled = false; status.Text = "Loading report…";
        try
        {
            var loaded = await Task.Run(() => ContentIndexService.ReadAsync(path, cancellation.Token), cancellation.Token);
            current = loaded; demo = false; reportPath.Text = Path.GetFullPath(path);
            SavePreferences(); UpdateHeader(); SetSection(section);
            status.Text = $"Loaded {reportPath.Text} · file updated {File.GetLastWriteTime(path):g}. Refresh reloads this snapshot; regenerate in Godot to reflect resource edits.";
        }
        catch (OperationCanceledException) { status.Text = "Load cancelled."; }
        catch (Exception ex) { status.Text = "Load failed: " + ex.Message + " Previous view retained."; }
        finally { cancellation.Dispose(); running = null; refresh.IsEnabled = true; }
    }
    private void SavePreferences()
    {
        preferences.ProjectRoot = project.Text?.Trim().Trim('"') ?? "";
        if (!demo) preferences.ReportPath = reportPath.Text ?? "";
        try { preferences.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Report is usable; preferences could not be saved: " + ex.Message; }
    }
    private void ShowDemo()
    {
        if (running != null) return;
        current = ContentIndexService.Demo(); demo = true; UpdateHeader(); SetSection("Items");
        status.Text = "Demo preview is an illustrative subset. LOAD JSON to inspect your actual exported content.";
    }
    private void UpdateHeader()
    {
        summary.Text = $"{current.Items.Count} ITEMS   /   {current.Sources.Count} SOURCES   /   {current.Recipes.Count} RECIPES   /   " +
            $"{current.Diagnostics.Count(x => x.Severity == "error")} ERRORS   /   {current.Diagnostics.Count(x => x.Severity == "warning")} WARNINGS";
        notice.Text = demo ? "DEMO PREVIEW — illustrative data, not the current project. File location actions are disabled." :
            current.Valid ? "REPORT VALIDATION PASSED · Snapshot only; configured biome selection does not guarantee spawning." :
            "REPORT HAS VALIDATION ERRORS · Browse Diagnostics before relying on these partial relationships.";
    }
    private void SetSection(string name)
    {
        section = name; updating = true;
        foreach (var pair in navigation) pair.Value.Background = Brush.Parse(pair.Key == name ? "#245369" : "#182D40");
        filter.ItemsSource = name is "Items" or "Recipes" ? new[] { "All", "Registered", "Unregistered" } :
            name == "Sources" ? new[] { "All", "Plant", "Tree", "Rock", "Ore", "Ground" } :
            name == "Diagnostics" ? new[] { "All", "Error", "Warning" } : new[] { "All", "Shared", "Unused" };
        filter.SelectedIndex = 0; search.Text = ""; updating = false; Filter();
    }
    private List<Entry> Entries() => section switch
    {
        "Items" => current.Items.Select(x => new Entry("Items", x.DisplayName ?? x.Id, $"{x.Id} · {(x.Registered ? "REGISTERED" : "UNREGISTERED")} · {x.Type}", x.Path, x)).ToList(),
        "Sources" => current.Sources.Select(x => new Entry("Sources", x.Id, $"{x.Kind} · {x.Rewards.Count} rewards · {x.Biomes.Count} biome bindings", x.Path, x)).ToList(),
        "Recipes" => current.Recipes.Select(x => new Entry("Recipes", x.Id, $"{x.Category} · {x.OutputItemId} ×{x.OutputCount} · {(x.Registered ? "REGISTERED" : "UNREGISTERED")}", x.Path, x)).ToList(),
        "Profiles" => current.Profiles.Select(x => new Entry("Profiles", FileName(x.Path), $"{(x.Shared ? "SHARED" : x.SourcePaths.Count == 0 ? "UNUSED" : "ASSIGNED")} · {x.Rewards.Count} drops · {x.SourcePaths.Count} sources", x.Path, x)).ToList(),
        _ => current.Diagnostics.Select(x => new Entry("Diagnostics", $"{x.Severity}: {x.Field}", x.Message, x.Path, x)).ToList()
    };
    private void Filter()
    {
        string query = search.Text?.Trim() ?? "", category = filter.SelectedItem as string ?? "All";
        string? previous = (browser.SelectedItem as Entry)?.Path;
        var entries = Entries().Where(x => x.Search.Contains(query, StringComparison.OrdinalIgnoreCase) && Matches(x.Value, category)).ToList();
        browser.ItemsSource = entries; browser.SelectedItem = entries.FirstOrDefault(x => x.Path == previous) ?? entries.FirstOrDefault();
        if (entries.Count == 0) { detail.Children.Clear(); detail.Children.Add(Label("No matching entries", 24)); }
        status.Text = $"{entries.Count} {section.ToLowerInvariant()} shown · {(demo ? "DEMO" : "READ ONLY")}";
    }
    private static bool Matches(object row, string category) => category == "All" || (row switch
    {
        ItemRow x => x.Registered == (category == "Registered"),
        RecipeRow x => x.Registered == (category == "Registered"),
        SourceRow x => string.Equals(x.Kind, category, StringComparison.OrdinalIgnoreCase),
        Diagnostic x => string.Equals(x.Severity, category, StringComparison.OrdinalIgnoreCase),
        ProfileRow x => category == "Shared" ? x.Shared : x.SourcePaths.Count == 0,
        _ => true
    });
    private static string FileName(string? locator) => string.IsNullOrEmpty(locator) ? "Resource" : locator.Split('/').Last();
    private static string N(double value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    private void Heading(string title) => detail.Children.Add(Label(title, 17, "#86E5F6"));
    private void Note(string text) => detail.Children.Add(Label(text, 13, "#9BB7CF"));
    private void Value(string name, object? value) => detail.Children.Add(Label(name + "  ·  " + (value?.ToString() ?? "—")));
    private void Show(Entry entry)
    {
        detail.Children.Clear(); detail.Children.Add(Label(entry.Kind.ToUpperInvariant() + " / " + (demo ? "DEMO" : "INSPECTION"), 12, "#86E5F6"));
        detail.Children.Add(Label(entry.Title, 26)); Note(entry.Subtitle); Locator(entry.Path);
        switch (entry.Value)
        {
            case ItemRow item:
                Heading("Inventory definition"); Value("Stable ID", item.Id); Value("Stack limit", item.MaxStack);
                Value("Weight", N(item.WeightKg) + " kg each"); Value("Volume", N(item.VolumeLitres) + " L each");
                Value("Capabilities", item.Capabilities.Count == 0 ? "Material / no primary use" : string.Join(", ", item.Capabilities));
                Heading("Registered through"); Locations(item.RegistrationPaths);
                Heading("Drop sources"); Links("Sources", item.SourcePaths);
                Heading("Recipes consuming this item"); Links("Recipes", item.ConsumedBy);
                Heading("Recipes producing this item"); Links("Recipes", item.ProducedBy); break;
            case SourceRow source:
                Heading("Extraction"); Note(source.Extraction); Value("Work", N(source.Work)); Value("Required strength", source.RequiredStrength);
                if (source.TotalUnits > 0) { Value("Deposit units", source.TotalUnits); Value("Units per batch", source.UnitsPerBatch); }
                Heading("Possible rewards"); Rewards(source.Rewards);
                Heading("Biome bindings");
                if (source.Biomes.Count == 0) Note(source.Kind == "ore" ? "Ore uses global surface settings; no per-biome ore rules yet." : source.Kind == "ground" ? "Ground uses its material catalog and generation rules." : "No biome binding found. The species is defined but may not be selected for generation.");
                foreach (var binding in source.Biomes)
                {
                    Heading(binding.BiomeId); Value("Family / weight", binding.Family + " / " + N(binding.Weight));
                    Value("Configured selection", binding.ConfiguredForSelection ? "Permitted by base catalog/biome/family/weight" : "Not configured for selection");
                    Value("Biome / family enabled", binding.BiomeEnabled + " / " + binding.FamilyEnabled);
                    Locator(binding.EditPath); Locations(binding.CatalogPaths);
                }
                Note("These bindings do not prove active campaign assignment or successful placement; climate, terrain and placement overrides still matter.");
                Heading("Registration / settings locations"); Locations(source.RegistrationPaths);
                foreach (var settings in current.OreSpawns.Where(x => x.DefinitionPath == source.Path))
                { Heading("Surface ore option"); Value("Enabled", settings.Enabled); Value("Chance before placement", N(settings.ChancePerChunk * 100) + "% per chunk"); Value("Attempts", settings.PlacementAttempts); Locator(settings.Path); Note(settings.Scope); }
                break;
            case RecipeRow recipe:
                Heading("Crafting recipe"); Note(recipe.Description); Value("Category", recipe.Category); Value("Duration", N(recipe.DurationSeconds) + " seconds");
                Heading("Ingredients");
                foreach (var ingredient in recipe.Ingredients) { ItemLink(ingredient.ItemId, ingredient.Count + " required"); Locator(ingredient.EditPath); }
                Heading("Output"); ItemLink(recipe.OutputItemId, recipe.OutputCount + " produced"); Locator(recipe.OutputPath);
                Heading("Registration"); Locations(recipe.RegistrationPaths); break;
            case ProfileRow profile:
                Heading(profile.Shared ? "Shared profile — edits would affect all linked sources" : "Harvest profile");
                Rewards(profile.Rewards); Heading("Referencing sources"); Links("Sources", profile.SourcePaths); break;
            case Diagnostic diagnostic:
                Heading(diagnostic.Severity == "error" ? "Validation error" : "Validation warning"); Note(diagnostic.Message); Value("Field", diagnostic.Field);
                Note("Correct the game resource, regenerate the report, then refresh here. This inspector cannot change it."); break;
        }
        var related = current.Diagnostics.Where(x => SameFile(x.Path, entry.Path)).ToList();
        if (entry.Value is not Diagnostic && related.Count > 0) { Heading("Related diagnostics"); foreach (var diagnostic in related) Note(diagnostic.Severity + " / " + diagnostic.Field + ": " + diagnostic.Message); }
    }
    private static bool SameFile(string? a, string? b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        a.Split(new[] { "::", "#" }, StringSplitOptions.None)[0] == b.Split(new[] { "::", "#" }, StringSplitOptions.None)[0];
    private void Rewards(List<RewardRow> rewards)
    {
        if (rewards.Count == 0) { Note("No rewards listed. An empty harvest profile can intentionally yield nothing."); return; }
        foreach (var reward in rewards)
        {
            ItemLink(reward.ItemId, $"{reward.MinimumCount}–{reward.MaximumCount}  ·  {N(reward.Chance * 100)}% chance");
            Note(reward.Context); Value("Field", reward.Field); Locator(reward.EditPath);
            var profile = current.Profiles.FirstOrDefault(x => reward.EditPath != null &&
                (reward.EditPath.StartsWith(x.Path + "::", StringComparison.Ordinal) || reward.EditPath.StartsWith(x.Path + "#", StringComparison.Ordinal)));
            if (profile != null) Link("Profiles", profile.Path, "View profile / " + profile.SourcePaths.Count + " source references");
        }
        Note("Drop chance is conditional on the extraction event. Ore primary yields use remaining units; bonuses roll once per successful batch.");
    }
    private void ItemLink(string? id, string caption)
    {
        var item = current.Items.FirstOrDefault(x => x.Id == id && x.Registered) ?? current.Items.FirstOrDefault(x => x.Id == id);
        if (item == null) { Note((id ?? "Missing item") + " · " + caption + " · not found in report"); return; }
        Link("Items", item.Path, (item.DisplayName ?? id) + "  ·  " + caption);
    }
    private void Links(string kind, List<string> paths)
    { if (paths.Count == 0) Note("None listed in this report."); foreach (string path in paths) Link(kind, path, FileName(path)); }
    private void Link(string kind, string path, string label)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => { SetSection(kind); var entry = Entries().FirstOrDefault(x => x.Path == path); if (entry == null) status.Text = "Linked definition is absent from this report."; else { browser.SelectedItem = entry; Show(entry); } };
        detail.Children.Add(button);
    }
    private void Locations(List<string> paths) { if (paths.Count == 0) Note("None listed."); foreach (string path in paths) Locator(path); }
    private void Locator(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Label(path, 12, "#7998B1"));
        var button = new Button { Content = "LOCATE FILE", FontSize = 11, Padding = new Thickness(8, 4), IsEnabled = !demo && Resolve(path) != null };
        button.Click += (_, _) => Locate(path); panel.Children.Add(button); detail.Children.Add(panel);
    }
    private string? Resolve(string path)
    { try { return ContentIndexService.ResolveResource(project.Text?.Trim().Trim('"') ?? "", path); } catch (Exception) { return null; } }
    private void Locate(string locator)
    {
        var path = Resolve(locator);
        if (path == null || demo) { status.Text = "Select the matching project folder; this source file is unavailable."; return; }
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
            status.Text = "Located containing file. Subresource locator: " + locator;
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void LocateReport()
    {
        if (demo) { status.Text = "Load a real report first."; return; }
        try { var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath.Text ?? "")); if (Directory.Exists(directory)) Process.Start(new ProcessStartInfo(directory!) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void Instructions()
    {
        detail.Children.Clear(); Heading("Connect your game export");
        Note("1. Apply Content Pass 6 in isometric-new-2d and rebuild the game.");
        Note("2. Open ITEMS/Discovery/ContentReportExport.tscn in Godot and press F6. The Output panel prints the report folder.");
        Note("3. In this inspector, choose PROJECT and FIND REPORT, or LOAD JSON and select ContentIndex.json.");
        Note("4. Browse Items, Sources, Recipes, Profiles and Diagnostics. Select linked cards to follow relationships.");
        Note("REFRESH REPORT reloads the JSON. To see changed game resources, export again in Godot first. It does not run the game/exporter automatically.");
        Note("This first version is read-only. Future editing, recipe creation and item registration can build on the same report model, with validation and change previews.");
        Note("Only the last report/project paths are remembered in toolbox preferences. No game files are changed.");
    }
}
