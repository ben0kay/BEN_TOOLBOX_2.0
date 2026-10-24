using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BEN_TOOLBOX_2_0.Services;
using BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;

namespace BEN_TOOLBOX_2_0.Views;

public partial class MainWindow : Window
{
    private readonly ToolLauncher _launcher = new();
    private ScreenshotHotkey? _screenshotHotkey;
    public MainWindow()
    {
        InitializeComponent();
        InitializeDashboard();
        ShowSection("Dashboard");
        if (OperatingSystem.IsWindows())
        {
            _screenshotHotkey = new ScreenshotHotkey(message => { InfoTitle.Text = "SCREENSHOT QUICK SAVE"; InfoDescription.Text = message; });
            if (!_screenshotHotkey.Registered) InfoDescription.Text = "Screenshot hotkey Ctrl+Shift+V unavailable (already registered by another application).";
        }
        Closed += (_, _) => _screenshotHotkey?.Dispose();
    }

    private void CloseToolbox(object? sender, RoutedEventArgs e) => Close();

    private void NavigateDashboard(object? sender, RoutedEventArgs e) => ShowSection("Dashboard");
    private void NavigateGame(object? sender, RoutedEventArgs e) => ShowSection("Game");
    private void NavigateFiles(object? sender, RoutedEventArgs e) => ShowSection("Files");
    private void NavigateInspect(object? sender, RoutedEventArgs e) => ShowSection("Inspect");
    private void NavigateSettings(object? sender, RoutedEventArgs e) => ShowSection("Settings");

    private async void LaunchTool(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ToolDefinition tool }) return;
        try
        {
            if (tool.Id == "internet-observatory")
            {
                new InternetObservatoryWindow().Show(this);
                InfoTitle.Text = "INTERNET OBSERVATORY";
                InfoDescription.Text = "USGS earthquake explorer with source timestamps and offline map.";
                return;
            }
            if (tool.Id == "clipboard-png")
            {
                new ClipboardPngWindow().Show(this);
                InfoTitle.Text = "CLIPBOARD TO PNG";
                InfoDescription.Text = "Use Win+Shift+S, then Ctrl+Shift+V to quick-save while the toolbox is running.";
                return;
            }
            if (tool.Id == "content-inspector")
            {
                new ContentInspectorWindow().Show(this);
                InfoTitle.Text = "ITEMS & RESOURCE INSPECTOR";
                InfoDescription.Text = "Read-only content browser opened. Load your game export to inspect items, drops, recipes and biome bindings.";
                return;
            }
            if (tool.Id == "lighting" || tool.Id == "biome")
            {
                new WorldInspectorWindow(tool.Id == "lighting" ? "lighting" : "biome").Show(this);
                InfoTitle.Text = "NATIVE WORLD INSPECTOR";
                InfoDescription.Text = tool.Name + " opened in Avalonia; the original PowerShell tool is preserved.";
                return;
            }
            if (tool.Id == "sprite-inventory")
            {
                new SpriteInventoryWindow().Show(this);
                InfoTitle.Text = "SPRITE INVENTORY";
                InfoDescription.Text = "Read-only image metadata browser opened under Project Inspectors.";
                return;
            }
            if (tool.Id == "file-size" || tool.Id == "resource-checker")
            {
                new InspectorWindow(tool.Id == "file-size" ? "size" : "resources").Show(this);
                InfoTitle.Text = "NATIVE INSPECTOR";
                InfoDescription.Text = tool.Name + " opened in Avalonia. The legacy PowerShell script remains available.";
                return;
            }
            if (tool.Id == "artwork-prompter")
            {
                new ArtworkPrompterWindow().Show(this);
                InfoTitle.Text = "NATIVE ARTWORK PROMPTER";
                InfoDescription.Text = "Fantasy artwork prompts and matching sibling variants are ready.";
                return;
            }
            if (tool.Id == "asset-usage" || tool.Id == "folder-backup")
            {
                new RemainingToolsWindow(tool.Id == "asset-usage" ? "usage" : "backup").Show(this);
                InfoTitle.Text = "NATIVE TOOL";
                InfoDescription.Text = tool.Name + " opened in Avalonia. The legacy PowerShell version is preserved.";
                return;
            }
            if (tool.Id == "youtube-audio-downloader")
            {
                new YouTubeAudioDownloaderWindow().Show(this);
                InfoTitle.Text = "YOUTUBE AUDIO DOWNLOADER";
                InfoDescription.Text = "Single-video MP3 downloads and manual yt-dlp updates.";
                return;
            }
            if (tool.Id == "seasonal-sprite-studio")
            {
                new SeasonalSpriteStudioWindow().Show(this);
                InfoTitle.Text = "SEASONAL SPRITE STUDIO";
                InfoDescription.Text = "Standalone foliage masks, protected trunk editing, mask projects and seasonal preview.";
                return;
            }
            if (tool.Id == "pixel-art-converter")
            {
                new PixelArtConverterWindow().Show(this);
                InfoTitle.Text = "PIXEL ART CONVERTER";
                InfoDescription.Text = "Pixel-detail preview, palettes, PNG export and direct Sprite Batch handoff. Original dimensions retained.";
                return;
            }
            if (tool.Id == "sprite-batch")
            {
                new SpriteBatchWindow().Show(this);
                InfoTitle.Text = "NATIVE SPRITE BATCH TOOL";
                InfoDescription.Text = "Sprite Batch Tool opened in Avalonia; original PowerShell utility is preserved.";
                return;
            }
            if (tool.Id == "sprite-splitter")
            {
                new SpriteSplitterWindow().Show(this);
                InfoTitle.Text = "NATIVE SPRITE SPLITTER";
                InfoDescription.Text = "Sprite Splitter now opens in Avalonia; the legacy PowerShell script remains available in the repository.";
                return;
            }
            if (tool.Id == "duplicates")
            {
                var native = new DuplicateFinderWindow();
                native.Show(this);
                InfoTitle.Text = "NATIVE DUPLICATE FINDER";
                InfoDescription.Text = "Native Avalonia duplicate finder opened. The old PowerShell tool remains in the repository.";
                return;
            }
            _launcher.Launch(tool);
            InfoTitle.Text = "TOOL LAUNCH REQUESTED";
            InfoDescription.Text = tool.Name + " opened in a separate Windows PowerShell process.";
        }
        catch (Exception ex)
        {
            var dialog = new Window
            {
                Title = "Unable to launch tool",
                Width = 550, Height = 230,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBlock
                {
                    Text = ex.Message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(24)
                }
            };
            await dialog.ShowDialog(this);
        }
    }

    private void ShowSection(string section)
    {
        DashboardNav.Classes.Set("active", section == "Dashboard");
        GameNav.Classes.Set("active", section == "Game");
        FilesNav.Classes.Set("active", section == "Files");
        InspectNav.Classes.Set("active", section == "Inspect");
        SettingsNav.Classes.Set("active", section == "Settings");

        var dashboard = section == "Dashboard";
        DashboardPanel.IsVisible = dashboard;
        SetDashboardActive(dashboard);
        ToolListPanel.IsVisible = section is "Game" or "Files" or "Inspect";
        ToolsView.ItemsSource = section switch
        {
            "Game" => ToolCatalog.ByCategory("Game"),
            "Files" => ToolCatalog.ByCategory("Files"),
            "Inspect" => ToolCatalog.ByCategory("Inspect"),
            _ => null
        };

        switch (section)
        {
            case "Game":
                PageKicker.Text = "WORKSPACE / GAME DEVELOPMENT";
                PageTitle.Text = "Game Development";
                PageSubtitle.Text = "Sprite processing, artwork prompts and resource utilities.";
                InfoTitle.Text = "GAME DEVELOPMENT / TOOL CATALOG";
                InfoDescription.Text = "Choose a tool to open its native Avalonia window or a preserved legacy utility.";
                break;
            case "Files":
                PageKicker.Text = "WORKSPACE / FILE MANAGEMENT";
                PageTitle.Text = "File Toolbox";
                PageSubtitle.Text = "Backups, duplicates and file management.";
                InfoTitle.Text = "FILE MANAGEMENT / EXTERNAL UTILITIES";
                InfoDescription.Text = "Review any destructive actions in the utility itself before confirming.";
                break;
            case "Inspect":
                PageKicker.Text = "WORKSPACE / INSPECTORS";
                PageTitle.Text = "Project Inspectors";
                PageSubtitle.Text = "Read project data, file sizes, lighting and asset references.";
                InfoTitle.Text = "INSPECTION / TOOL CATALOG";
                InfoDescription.Text = "Some inspectors expect a Godot project folder to be selected.";
                break;
            case "Settings":
                PageKicker.Text = "WORKSPACE / SETTINGS";
                PageTitle.Text = "Settings";
                PageSubtitle.Text = "Preferences and application customization.";
                InfoTitle.Text = "FUTURE SETTINGS";
                InfoDescription.Text = "Appearance controls, saved preferences and toolbox paths will follow.";
                break;
            default:
                PageKicker.Text = "CONTROL DECK  /  OVERVIEW";
                PageTitle.Text = "Command Center";
                PageSubtitle.Text = "Choose a workspace to get started. Computer information is shown below.";
                InfoTitle.Text = "BTX-02   /   COMMAND CENTER";
                InfoDescription.Text = "Pick a workspace above, or use the left navigation. CPU and memory charts show recent readings from this PC.";
                break;
        }
    }
}

