using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

// Resource Integrity and Sprite Inventory are separate read-only tabs.
// The File Size Inspector continues to use the same window in "size" mode.
public sealed class InspectorWindow : Window
{
    private readonly string kind;
    private readonly TextBox folder = new() { PlaceholderText = "Select folder containing project.godot" };
    private readonly Button scan = new() { Content = "SCAN / REFRESH" };
    private readonly Button scanSprites = new() { Content = "SCAN SPRITES" };
    private readonly Button cancel = new() { Content = "CANCEL", IsEnabled = false };
    private readonly Button cancelSprites = new() { Content = "CANCEL", IsEnabled = false };
    private readonly TextBlock status = new() { Text = "Ready", TextWrapping = TextWrapping.Wrap };
    private readonly ListBox rows = new() { Height = 405 };
    private readonly ListBox spriteRows = new() { Height = 365 };
    private readonly TextBox spriteSearch = new() { PlaceholderText = "Name or folder...", Width = 235 };
    private readonly TextBox minEdge = new() { PlaceholderText = "0", Width = 100 };
    private readonly TextBox minSize = new() { PlaceholderText = "0", Width = 100 };
    private readonly ComboBox sort = new()
    {
        ItemsSource = new[] { "Name (A–Z)", "Largest dimensions", "Largest file size" },
        SelectedIndex = 0, MinWidth = 170
    };
    private readonly TextBlock inventorySummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock inventoryIssues = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#EDBA89") };
    private readonly TextBlock previewInfo = new() { Text = "Select a sprite to preview.", TextWrapping = TextWrapping.Wrap };
    private readonly Image previewImage = new() { Width = 300, Height = 260, Stretch = Stretch.Uniform };
    private CancellationTokenSource? running;
    private CancellationTokenSource? previewCancel;
    private Bitmap? currentPreview;
    private SpriteInventoryResult? inventory;
    private bool closed;

    public InspectorWindow(string kind)
    {
        this.kind = kind;
        Title = kind == "size" ? "File Size Inspector / Native" : "Resource Checker / Native";
        Width = 1140;
        Height = 820;
        MinWidth = 800;
        MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#091522");
        Foreground = Brush.Parse("#E8F4FF");

        var container = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        container.Children.Add(new TextBlock
        {
            Text = kind == "size" ? "FILE SIZE INSPECTOR / NATIVE" : "RESOURCE CHECKER / NATIVE",
            FontSize = 23, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#8BE5F6")
        });
        container.Children.Add(new TextBlock
        {
            Text = kind == "size"
                ? "Read-only file size ranking; generated and version-control directories are skipped."
                : "Scan broken res:// links or inspect image sizes in the same Godot project. Neither tab changes any files.",
            Foreground = Brush.Parse("#A8BED0"), TextWrapping = TextWrapping.Wrap
        });

        var picker = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120"), ColumnSpacing = 10 };
        picker.Children.Add(folder);
        var browse = new Button { Content = "BROWSE" };
        browse.Click += async (_, _) =>
        {
            var found = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (found.Count > 0 && found[0].Path.IsFile)
                folder.Text = found[0].Path.LocalPath;
        };
        Grid.SetColumn(browse, 1);
        picker.Children.Add(browse);
        container.Children.Add(picker);

        if (kind == "size")
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            actions.Children.Add(scan);
            actions.Children.Add(cancel);
            container.Children.Add(actions);
            container.Children.Add(status);
            SetupIntegrityRows();
            container.Children.Add(rows);
            scan.Click += async (_, _) => await RunScanAsync(false);
        }
        else
        {
            // Separate controls and independent scan results for both tabs.
            var integrityTab = new TabItem { Header = "RESOURCE INTEGRITY", Content = BuildIntegrityTab() };
            var spriteTab = new TabItem { Header = "SPRITE INVENTORY", Content = BuildSpriteTab() };
            container.Children.Add(new TabControl { ItemsSource = new[] { integrityTab, spriteTab } });
            container.Children.Add(status);

            scan.Click += async (_, _) => await RunScanAsync(false);
            scanSprites.Click += async (_, _) => await RunScanAsync(true);
            spriteRows.SelectionChanged += async (_, _) => await ShowPreviewAsync();
            spriteSearch.TextChanged += (_, _) => ApplySpriteFilter();
            minEdge.TextChanged += (_, _) => ApplySpriteFilter();
            minSize.TextChanged += (_, _) => ApplySpriteFilter();
            sort.SelectionChanged += (_, _) => ApplySpriteFilter();
        }

        Content = new ScrollViewer { Content = container };
        cancel.Click += (_, _) => running?.Cancel();
        cancelSprites.Click += (_, _) => running?.Cancel();
        Closed += (_, _) =>
        {
            closed = true;
            running?.Cancel();
            previewCancel?.Cancel();
            previewCancel?.Dispose();
            ClearPreview();
        };
    }

    private void SetupIntegrityRows()
    {
        rows.ItemTemplate = new FuncDataTemplate<InspectorRow>((item, _) =>
            new TextBlock
            {
                Text = item.Display, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(7),
                Foreground = Brush.Parse("#BCEAF5")
            }, true);
    }

    private Control BuildIntegrityTab()
    {
        SetupIntegrityRows();
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(8) };
        content.Children.Add(new TextBlock
        {
            Text = "Find missing static Godot res:// paths. Dynamic references, resource UIDs and runtime errors are outside this scan.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#A8BED0")
        });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        bar.Children.Add(scan);
        bar.Children.Add(cancel);
        content.Children.Add(bar);
        content.Children.Add(rows);
        return content;
    }

    private Control BuildSpriteTab()
    {
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        content.Children.Add(new TextBlock
        {
            Text = "Metadata-only scan: PNG, WebP, JPEG, GIF and BMP. No full images are loaded until selected; generated folders and symlinks are skipped.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#A8BED0")
        });
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        commands.Children.Add(scanSprites);
        commands.Children.Add(cancelSprites);
        content.Children.Add(commands);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        filters.Children.Add(FilterField("SEARCH FILE / PATH", spriteSearch));
        filters.Children.Add(FilterField("MIN LONG EDGE (PX)", minEdge));
        filters.Children.Add(FilterField("MIN SIZE (MiB)", minSize));
        filters.Children.Add(FilterField("SORT BY", sort));
        content.Children.Add(filters);
        content.Children.Add(inventorySummary);

        spriteRows.ItemTemplate = new FuncDataTemplate<SpriteInventoryEntry>((item, _) =>
        {
            var panel = new StackPanel { Spacing = 2, Margin = new Thickness(5) };
            panel.Children.Add(new TextBlock
            {
                Text = $"{item.Name}    |    {item.Dimensions} px    |    {item.FileSize}    |    {item.Format}",
                FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#D6F0FF"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            panel.Children.Add(new TextBlock
            {
                Text = item.RelativePath, FontSize = 11, Foreground = Brush.Parse("#A8BED0"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return panel;
        }, true);

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 14 };
        main.Children.Add(spriteRows);
        var details = new StackPanel { Spacing = 9 };
        details.Children.Add(new TextBlock
        {
            Text = "SELECTED SPRITE", FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#86E5F6")
        });
        details.Children.Add(new Border
        {
            Background = Brush.Parse("#203044"), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4), Child = previewImage
        });
        details.Children.Add(previewInfo);
        Grid.SetColumn(details, 1);
        main.Children.Add(details);
        content.Children.Add(main);
        content.Children.Add(inventoryIssues);
        return content;
    }

    private static StackPanel FilterField(string label, Control control)
    {
        var result = new StackPanel { Spacing = 4 };
        result.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brush.Parse("#9DCBDD") });
        result.Children.Add(control);
        return result;
    }

    private string Root => folder.Text?.Trim().Trim('"') ?? "";

    private async Task RunScanAsync(bool sprites)
    {
        if (running is not null) return;
        running = new CancellationTokenSource();
        var token = running.Token;
        scan.IsEnabled = false;
        scanSprites.IsEnabled = false;
        cancel.IsEnabled = true;
        cancelSprites.IsEnabled = true;
        status.Text = sprites ? "Scanning image headers (no image decoding)..." : "Scanning...";
        if (sprites)
        {
            spriteRows.ItemsSource = null;
            inventory = null;
            inventorySummary.Text = "";
            inventoryIssues.Text = "";
            CancelPreview();
        }
        else
            rows.ItemsSource = null;
        try
        {
            var root = Root;
            if (sprites)
            {
                inventory = await SpriteInventoryService.ScanAsync(root, token);
                ApplySpriteFilter();
                status.Text = $"Sprite scan complete / {inventory.Sprites.Count:N0} sprites, {inventory.Issues.Count:N0} issues.";
            }
            else
            {
                List<InspectorRow> result = kind == "size"
                    ? await InspectorServices.LargestFilesAsync(root, 200, token)
                    : await InspectorServices.CheckResourcesAsync(root, token);
                rows.ItemsSource = result;
                status.Text = $"Scan complete / {result.Count:N0} results.";
            }
        }
        catch (OperationCanceledException)
        {
            status.Text = "Scan cancelled. Incomplete results were discarded.";
        }
        catch (Exception e)
        {
            status.Text = "Scan error: " + e.Message;
        }
        finally
        {
            running?.Dispose();
            running = null;
            scan.IsEnabled = true;
            scanSprites.IsEnabled = true;
            cancel.IsEnabled = false;
            cancelSprites.IsEnabled = false;
        }
    }

    private void ApplySpriteFilter()
    {
        if (inventory is null) return;
        var text = spriteSearch.Text?.Trim() ?? "";
        if (!TryMinimum(minEdge.Text, out var pixels)
            || !TryMinimum(minSize.Text, out var mib))
        {
            inventorySummary.Text = "Filters require non-negative numbers.";
            spriteRows.ItemsSource = Array.Empty<SpriteInventoryEntry>();
            return;
        }

        var subset = inventory.Sprites.Where(item =>
            (text.Length == 0 || item.RelativePath.Contains(text, StringComparison.OrdinalIgnoreCase))
            && Math.Max(item.Width, item.Height) >= pixels
            && item.Bytes >= mib * 1024 * 1024);
        subset = sort.SelectedIndex switch
        {
            1 => subset.OrderByDescending(item => item.PixelCount).ThenBy(item => item.RelativePath),
            2 => subset.OrderByDescending(item => item.Bytes).ThenBy(item => item.RelativePath),
            _ => subset.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
        };
        var shown = subset.ToArray();
        var selected = spriteRows.SelectedItem as SpriteInventoryEntry;
        spriteRows.ItemsSource = shown;
        if (selected is not null && shown.Contains(selected))
            spriteRows.SelectedItem = selected;
        inventorySummary.Text =
            $"{shown.Length:N0} of {inventory.Sprites.Count:N0} sprites shown  |  " +
            $"{inventory.TotalBytes / (1024.0 * 1024.0):N2} MiB total on disk  |  " +
            $"{inventory.Issues.Count:N0} unreadable image(s)/folder(s).";
        inventoryIssues.Text = inventory.Issues.Count == 0 ? ""
            : "Problems encountered (first 8):" + Environment.NewLine
              + string.Join(Environment.NewLine, inventory.Issues.Take(8)
                  .Select(x => $"{x.RelativePath}: {x.Reason}"));
    }

    private static bool TryMinimum(string? text, out double number)
    {
        if (string.IsNullOrWhiteSpace(text)) { number = 0; return true; }
        return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            && double.IsFinite(number) && number >= 0;
    }

    private void CancelPreview()
    {
        previewCancel?.Cancel();
        previewCancel?.Dispose();
        previewCancel = null;
        ClearPreview();
    }

    private void ClearPreview()
    {
        previewImage.Source = null;
        currentPreview?.Dispose();
        currentPreview = null;
        previewInfo.Text = "Select a sprite to preview.";
    }

    private async Task ShowPreviewAsync()
    {
        CancelPreview();
        if (spriteRows.SelectedItem is not SpriteInventoryEntry entry || closed) return;
        previewInfo.Text = entry.RelativePath + Environment.NewLine +
            $"{entry.Dimensions} px • {entry.FileSize} • {entry.Format}";
        // Avoid surprise multi-GB allocations from enormous or malformed images.
        if (entry.PixelCount > 40_000_000 || entry.Bytes > 64L * 1024 * 1024)
        {
            previewInfo.Text += Environment.NewLine + "Preview disabled for unusually large images.";
            return;
        }

        var cts = new CancellationTokenSource();
        previewCancel = cts;
        var token = cts.Token;
        try
        {
            var bitmap = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = new FileStream(entry.FullPath, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoded = Bitmap.DecodeToWidth(stream, Math.Min(320, entry.Width));
                return decoded;
            }, token);

            if (token.IsCancellationRequested || closed)
            {
                bitmap.Dispose();
                return;
            }
            currentPreview = bitmap;
            previewImage.Source = bitmap;
        }
        catch (OperationCanceledException) { /* Another sprite was selected. */ }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested && !closed)
                previewInfo.Text += Environment.NewLine + "Preview unavailable: " + e.Message;
        }
    }
}
