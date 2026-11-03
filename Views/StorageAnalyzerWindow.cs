using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Services;
using BEN_TOOLBOX_2_0.Shared.UI;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Controls;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Treemap;

namespace BEN_TOOLBOX_2_0.Views;

/// <summary>
/// SA2 interactive, read-only treemap over the SA1 scanner's retained hierarchy.
/// Fictional preview and actual scan results are kept clearly separate.
/// </summary>
public sealed class StorageAnalyzerWindow : Window
{
    private static readonly IBrush Cyan = Brush.Parse("#53E1E9");
    private static readonly IBrush Muted = Brush.Parse("#98B8C8");
    private readonly StorageScanService scanner = new();
    private readonly ComboBox drivePicker = new() { MinWidth = 180 };
    private readonly Button scan = BtxUiKit.CreateButton("SCAN SELECTED DRIVE", BtxUiKit.ButtonKind.Primary);
    private readonly Button cancel = BtxUiKit.CreateButton("CANCEL SCAN", BtxUiKit.ButtonKind.Danger);
    private readonly Button demo = BtxUiKit.CreateButton("SHOW DESIGN PREVIEW");
    private readonly CheckBox includeHidden = new()
    {
        Content = "Include hidden / system entries", IsChecked = true
    };
    private readonly CheckBox developerPreset = new()
    {
        Content = "Exclude development caches", IsChecked = false
    };
    private readonly TextBox excludedFolders = new()
    {
        Watermark = "e.g. cache, build, Temporary*"
    };
    private readonly TextBox excludedExtensions = new()
    {
        Watermark = "e.g. .tmp, .log, *.bak"
    };
    private readonly TextBox excludedPaths = new()
    {
        Watermark = "One full folder or file path per line", AcceptsReturn = true,
        Height = 70, TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock state = new()
    {
        Text = "NO SCAN STARTED", Foreground = Cyan, FontWeight = FontWeight.SemiBold
    };
    private readonly TextBlock driveDetails = new()
    {
        Text = "Choose a drive. No files will be read.",
        Foreground = Muted, TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock scanActivity = new()
    {
        Text = "Ready. Scans start only when you click Scan.",
        Foreground = Muted, TextWrapping = TextWrapping.Wrap
    };
    private readonly ProgressBar activityBar = new()
    {
        IsIndeterminate = true, IsVisible = false, Height = 8
    };
    private readonly StackPanel mapContent = new() { Spacing = 9 };
    private readonly StackPanel itemsContent = new() { Spacing = 5 };
    private readonly StackPanel selectedContent = new() { Spacing = 8 };
    private readonly StackPanel typesContent = new() { Spacing = 8 };
    private readonly StackPanel categoriesContent = new() { Spacing = 8 };
    private readonly StackPanel errorsContent = new() { Spacing = 6 };
    private readonly StorageTreemapControl treemap = new();
    private readonly ComboBox minimumPreset = new()
    {
        ItemsSource = new[] { "All files", "1 KiB", "10 KiB", "100 KiB",
            "1 MiB", "10 MiB", "100 MiB", "Custom…" },
        SelectedIndex = 0, MinWidth = 120
    };
    private readonly TextBox customAmount = new()
    {
        Text = "10", Width = 75, Watermark = "Value", IsVisible = false
    };
    private readonly ComboBox customUnit = new()
    {
        ItemsSource = new[] { "KiB", "MiB", "GiB" },
        SelectedIndex = 1, Width = 86, IsVisible = false
    };
    private readonly Button applyCustom = BtxUiKit.CreateButton("APPLY", BtxUiKit.ButtonKind.Secondary);
    private readonly TextBlock mapStatistics = new()
    {
        Text = "No file-level map generated.", Foreground = Muted,
        TextWrapping = TextWrapping.Wrap
    };
    private long appliedMinimumBytes;
    private static readonly long[] MinimumPresets =
    {
        0, 1024, 10 * 1024, 100 * 1024,
        1024 * 1024, 10 * 1024 * 1024, 100 * 1024 * 1024
    };
    private readonly WrapPanel breadcrumbs = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly TextBlock hoverDetails = new()
    {
        Text = "Hover a tile for size and path. Click to select; double-click folders to zoom.",
        Foreground = Muted, TextWrapping = TextWrapping.Wrap
    };
    private readonly Button upFolder = BtxUiKit.CreateButton("UP ONE LEVEL", BtxUiKit.ButtonKind.Secondary);
    private readonly Button zoomFolder = BtxUiKit.CreateButton("ZOOM INTO SELECTED", BtxUiKit.ButtonKind.Primary);
    private StorageNode? focusDirectory;
    private StorageNode? selectedNode;
    private IReadOnlyList<TreemapEntry> focusedTopEntries = Array.Empty<TreemapEntry>();
    private StorageScanResult? result;
    private CancellationTokenSource? activeScan;
    private bool previewActive;
    private bool closed;
    private long scanGeneration;

    private sealed record DemoItem(string Name, string Size, double Weight, string Color);
    private static readonly DemoItem[] Demo =
    {
        new("Games", "129.4 GB", 34, "#8649D5"),
        new("Program Files", "82.7 GB", 24, "#287DCF"),
        new("Users", "56.3 GB", 15, "#149B83"),
        new("Windows", "40.6 GB", 12, "#D98A32"),
        new("Projects", "26.8 GB", 8, "#BF498F"),
        new("Media", "18.9 GB", 7, "#2D9AA7")
    };

    public StorageAnalyzerWindow()
    {
        Title = "BTX Storage Analyzer / Files";
        Width = 1480; Height = 920; MinWidth = 980; MinHeight = 650;
        Background = Brush.Parse("#07131F");
        Foreground = Brush.Parse("#E9F9FF");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        BtxUiKit.ApplyInputStyle(drivePicker);
        BtxUiKit.ApplyInputStyle(excludedFolders);
        BtxUiKit.ApplyInputStyle(excludedExtensions);
        BtxUiKit.ApplyInputStyle(excludedPaths);
        BtxUiKit.ApplyInputStyle(minimumPreset);
        BtxUiKit.ApplyInputStyle(customAmount);
        BtxUiKit.ApplyInputStyle(customUnit);
        applyCustom.IsVisible = false;
        activityBar.Classes.Add("btx-progress");
        includeHidden.Classes.Add("btx-check");
        developerPreset.Classes.Add("btx-check");

        var page = new StackPanel { Margin = new Thickness(22), Spacing = 16 };
        page.Children.Add(new TextBlock
        {
            Text = "FILES  /  BTX STORAGE ANALYZER  /  SA2",
            Foreground = Cyan, FontSize = 12, LetterSpacing = 1.5
        });
        page.Children.Add(new TextBlock
        {
            Text = "STORAGE ANALYZER", FontSize = 30, FontWeight = FontWeight.Bold
        });
        page.Children.Add(new TextBlock
        {
            Text = "Scan disk usage · Explore folders visually · Double-click a folder to drill down",
            Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap
        });

        var toolbar = new WrapPanel { ItemSpacing = 12, LineSpacing = 10 };
        toolbar.Children.Add(new TextBlock
        {
            Text = "TARGET DRIVE", Foreground = Cyan,
            VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold
        });
        PopulateDrives();
        toolbar.Children.Add(drivePicker);
        toolbar.Children.Add(scan);
        cancel.IsEnabled = false;
        toolbar.Children.Add(cancel);
        toolbar.Children.Add(demo);
        toolbar.Children.Add(state);
        page.Children.Add(Card(toolbar, emphasized: true));

        // Live activity belongs near the scan button, not below exclusions.
        var progressPanel = new StackPanel { Spacing = 8 };
        progressPanel.Children.Add(scanActivity);
        progressPanel.Children.Add(activityBar);
        page.Children.Add(Card(progressPanel));

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,*,275"), ColumnSpacing = 13
        };
        var left = new StackPanel { Spacing = 13 };
        left.Children.Add(Section("DRIVE SELECTION", driveDetails));
        left.Children.Add(Section("SCAN SETTINGS", new StackPanel
        {
            Spacing = 9,
            Children =
            {
                new TextBlock { Text = "Recursive file metadata scan. Junctions / reparse points are always skipped.",
                    Foreground = Muted, TextWrapping = TextWrapping.Wrap },
                includeHidden,
                developerPreset,
                new TextBlock { Text = "Preset: .git, .godot, node_modules, bin, obj",
                    Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap }
            }
        }));

        var filterFields = new StackPanel { Spacing = 10 };
        filterFields.Children.Add(BtxUiKit.CreateField("EXCLUDE FOLDER NAMES / WILDCARDS", excludedFolders));
        filterFields.Children.Add(BtxUiKit.CreateField("EXCLUDE FILE EXTENSIONS", excludedExtensions));
        filterFields.Children.Add(BtxUiKit.CreateField("EXCLUDE EXACT PATHS / SUBTREES", excludedPaths));
        filterFields.Children.Add(new TextBlock
        {
            Text = "Folder patterns and extensions are comma-separated. Paths use one line each. Exclusions are applied before directory traversal; excluded sizes are unknown.",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        left.Children.Add(Section("EXCLUSIONS  /  BEFORE SCAN", filterFields));

        left.Children.Add(Section("ERRORS / SKIPPED PATHS", errorsContent));
        layout.Children.Add(left);

        var middle = new StackPanel { Spacing = 13 };
        middle.Children.Add(Section("DISK USAGE TREEMAP", mapContent));
        middle.Children.Add(Section("LARGEST ITEMS / CURRENT FOLDER", itemsContent));
        Grid.SetColumn(middle, 1);
        layout.Children.Add(middle);

        var right = new StackPanel { Spacing = 13 };
        right.Children.Add(Section("SELECTED FILE / FOLDER", selectedContent));
        right.Children.Add(Section("FILE TYPE BREAKDOWN  /  SA3", typesContent));
        right.Children.Add(Section("LARGEST CATEGORIES  /  SA3", categoriesContent));
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);

        page.Children.Add(layout);
        page.Children.Add(new TextBlock
        {
            Text = "SA2: read-only interactive treemap. Sizes are logical included file bytes, not allocated-on-disk space. Exclusions and inaccessible entries are omitted. No deletion, Explorer actions or automatic scans.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        Content = new ScrollViewer { Content = page };

        treemap.LayoutFailed += message =>
        {
            if (!closed && !previewActive && result is not null)
                mapStatistics.Text = "Treemap could not be rendered: " + message;
        };
        treemap.LayoutReady += snapshot =>
        {
            if (closed || previewActive || result is null) return;
            mapStatistics.Text =
                $"Mapped {snapshot.VisibleFiles:N0} files · {FormatBytes(snapshot.VisibleBytes)} shown" +
                (snapshot.MinimumFileBytes > 0
                    ? $"  |  {snapshot.HiddenFiles:N0} below threshold ({FormatBytes(snapshot.HiddenBytes)}) hidden from map"
                    : "") +
                (snapshot.ZeroByteFiles > 0
                    ? $"  |  {snapshot.ZeroByteFiles:N0} zero-byte files (no area)" : "") +
                $"  |  all original scan totals unchanged.";
        };
        minimumPreset.SelectionChanged += (_, _) =>
        {
            bool custom = minimumPreset.SelectedIndex == MinimumPresets.Length;
            customAmount.IsVisible = custom;
            customUnit.IsVisible = custom;
            applyCustom.IsVisible = custom;
            if (!custom && minimumPreset.SelectedIndex >= 0)
            {
                appliedMinimumBytes = MinimumPresets[minimumPreset.SelectedIndex];
                ApplyMinimumToMap();
            }
        };
        applyCustom.Click += (_, _) => ApplyCustomMinimum();
        treemap.NodeSelected += SelectActualNode;
        treemap.DirectoryRequested += NavigateToFolder;
        treemap.HoverChanged += item =>
        {
            hoverDetails.Text = item is null
                ? "Hover a tile for size and path. Click to select; double-click folders to zoom."
                : item.IsAggregate
                    ? $"{item.Label} · {FormatBytes(item.LogicalBytes)} · Grouped smaller entries"
                    : $"{item.Label} · {FormatBytes(item.LogicalBytes)}";
        };
        upFolder.Click += (_, _) =>
        {
            if (focusDirectory?.Parent is { } parent)
                NavigateToFolder(parent);
        };
        zoomFolder.Click += (_, _) =>
        {
            if (selectedNode is { IsDirectory: true } target)
                NavigateToFolder(target);
        };

        drivePicker.SelectionChanged += (_, _) =>
        {
            focusDirectory = null;
            selectedNode = null;
            focusedTopEntries = Array.Empty<TreemapEntry>();
            treemap.Clear();
            mapStatistics.Text = "No file-level map generated.";
            breadcrumbs.Children.Clear(); // Drop old scan breadcrumb references.
            result = null;
            previewActive = false;
            demo.Content = "SHOW DESIGN PREVIEW";
            UpdateDriveDetails();
            scanActivity.Text = "Ready. Click Scan to enumerate filesystem metadata.";
            Render();
        };
        scan.Click += async (_, _) => await StartScanAsync();
        cancel.Click += (_, _) =>
        {
            if (activeScan is null) return;
            activeScan.Cancel();
            cancel.IsEnabled = false;
            state.Text = "CANCELLING  /  FINALIZING PARTIAL RESULTS";
        };
        demo.Click += (_, _) =>
        {
            previewActive = !previewActive;
            demo.Content = previewActive ? "CLEAR DESIGN PREVIEW" : "SHOW DESIGN PREVIEW";
            Render();
        };
        Closed += (_, _) =>
        {
            closed = true;
            scanGeneration++;
            activeScan?.Cancel();
            treemap.Clear();
        };

        UpdateDriveDetails();
        Render();
    }

    private void PopulateDrives()
    {
        // Drive listing and capacity inspection do NOT enumerate files.
        var drives = new List<string> { "Select a drive..." };
        try
        {
            drives.AddRange(DriveInfo.GetDrives().Select(d => d.Name));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        drivePicker.ItemsSource = drives;
        drivePicker.SelectedIndex = 0;
    }

    private string? SelectedDrive()
    {
        var name = drivePicker.SelectedItem as string;
        return name is null or "Select a drive..." ? null : name;
    }

    private void UpdateDriveDetails()
    {
        var name = SelectedDrive();
        if (name is null)
        {
            driveDetails.Text = "Choose a drive. No files will be read.";
            RefreshControls();
            return;
        }

        try
        {
            var drive = new DriveInfo(name);
            driveDetails.Text = drive.IsReady
                ? $"{drive.Name} / {drive.DriveFormat}\nCapacity: {FormatBytes(drive.TotalSize)}\nFree: {FormatBytes(drive.AvailableFreeSpace)}\nDisk-used is not the same as scanned file bytes."
                : $"{name}\nDrive not ready.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            driveDetails.Text = $"{name}\nDrive metadata unavailable: {e.Message}";
        }
        RefreshControls();
    }

    private void RefreshControls()
    {
        bool busy = activeScan is not null;
        scan.IsEnabled = !busy && SelectedDrive() is not null;
        cancel.IsEnabled = busy && activeScan?.IsCancellationRequested != true;
        demo.IsEnabled = !busy;
        drivePicker.IsEnabled = !busy;
        includeHidden.IsEnabled = !busy;
        developerPreset.IsEnabled = !busy;
        excludedFolders.IsEnabled = !busy;
        excludedExtensions.IsEnabled = !busy;
        excludedPaths.IsEnabled = !busy;
        activityBar.IsVisible = busy;
    }

    private StorageScanOptions ReadOptions()
    {
        var drive = SelectedDrive() ?? throw new InvalidOperationException("Choose a drive first.");
        var folderNames = SplitCommaList(excludedFolders.Text);
        if (developerPreset.IsChecked == true)
            folderNames.AddRange(new[] { ".git", ".godot", "node_modules", "bin", "obj" });
        return new StorageScanOptions(
            new[] { drive },
            (excludedPaths.Text ?? "").Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            folderNames,
            SplitCommaList(excludedExtensions.Text),
            includeHidden.IsChecked == true);
    }

    private static List<string> SplitCommaList(string? text) =>
        (text ?? "").Split(new[] { ',', ';', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private async Task StartScanAsync()
    {
        if (closed || activeScan is not null || SelectedDrive() is null)
            return;

        StorageScanOptions options;
        try { options = ReadOptions(); }
        catch (Exception e)
        {
            state.Text = "INVALID SCAN OPTIONS";
            scanActivity.Text = e.Message;
            return;
        }

        var id = ++scanGeneration;
        var cts = new CancellationTokenSource();
        activeScan = cts;
        result = null;
        focusDirectory = null;
        selectedNode = null;
        focusedTopEntries = Array.Empty<TreemapEntry>();
        treemap.Clear();
        mapStatistics.Text = "Scanning; file-level layout will build after completion.";
        breadcrumbs.Children.Clear(); // Release previous scan hierarchy immediately.
        previewActive = false;
        demo.Content = "SHOW DESIGN PREVIEW";
        Render();
        state.Text = "SCANNING  /  READ ONLY";
        scanActivity.Text = "Scanning metadata. Number of items is unknown; no percentage is estimated.";
        errorsContent.Children.Clear();
        errorsContent.Children.Add(MutedText("Access and exclusion warnings will appear here."));
        RefreshControls();

        try
        {
            // Scanner reports at most about 4 updates per second. Each report
            // contains counters only; the live UI never traverses partial nodes.
            Action<StorageScanProgress> report = data => Dispatcher.UIThread.Post(() =>
            {
                if (closed || id != scanGeneration || activeScan is null)
                    return;
                scanActivity.Text = $"{data.Files:N0} files · {data.Directories:N0} folders\n{FormatBytes(data.LogicalBytes)} discovered · {data.Elapsed.TotalSeconds:N1}s\nFiltered: {data.FilteredEntries:N0} · Reparse: {data.ReparseEntries:N0} · Errors: {data.InaccessibleEntries:N0}\nNow: {ShortPath(data.CurrentDirectory)}";
            });

            var snapshot = await scanner.ScanAsync(options, report, cts.Token);
            if (closed || id != scanGeneration)
                return;

            result = snapshot;
            Render();
            state.Text = snapshot.Cancelled ? "CANCELLED  /  PARTIAL SCAN"
                : snapshot.InaccessibleEntries > 0 ? "COMPLETE  /  WITH ERRORS"
                : "SCAN COMPLETE";
            scanActivity.Text = $"{snapshot.Files:N0} files · {snapshot.Directories:N0} folders\n{FormatBytes(snapshot.LogicalBytes)} measured logical bytes · {snapshot.Elapsed.TotalSeconds:N1}s\n{snapshot.FilteredEntries:N0} excluded/hidden · {snapshot.ReparseEntries:N0} reparse skipped · {snapshot.InaccessibleEntries:N0} errors";
        }
        catch (Exception e)
        {
            if (!closed && id == scanGeneration)
            {
                Render();
                state.Text = "SCAN FAILED";
                scanActivity.Text = e.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(activeScan, cts))
                activeScan = null;
            cts.Dispose();
            if (!closed)
                RefreshControls();
        }
    }

    private static string ShortPath(string path) =>
        path.Length <= 74 ? path : "…" + path[^73..];

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:N1} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):N1} MiB";
        return $"{bytes / (1024d * 1024 * 1024):N2} GiB";
    }

    private static TextBlock MutedText(string text) => new()
    {
        Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap
    };

    private void Render()
    {
        mapContent.Children.Clear();
        itemsContent.Children.Clear();
        selectedContent.Children.Clear();
        typesContent.Children.Clear();
        categoriesContent.Children.Clear();
        errorsContent.Children.Clear();

        if (previewActive)
        {
            RenderDemo();
            state.Text = "DEMO DATA  /  NOT SCANNED";
            scanActivity.Text = "Design preview only. The numbers and names below are fictional.";
            errorsContent.Children.Add(MutedText("No real filesystem scan was performed."));
            return;
        }

        if (result is { } actual)
        {
            RenderActual(actual);
            state.Text = actual.Cancelled ? "CANCELLED  /  PARTIAL SCAN"
                : actual.InaccessibleEntries > 0 ? "COMPLETE  /  WITH ERRORS"
                : "SCAN COMPLETE";
            scanActivity.Text = $"{actual.Files:N0} files · {actual.Directories:N0} folders\n{FormatBytes(actual.LogicalBytes)} measured logical bytes · {actual.Elapsed.TotalSeconds:N1}s\n{actual.FilteredEntries:N0} excluded/hidden · {actual.ReparseEntries:N0} reparse skipped · {actual.InaccessibleEntries:N0} errors";
            return;
        }

        mapContent.Children.Add(Empty("Scan a selected drive to display proportional, interactive folder rectangles.", 315));
        itemsContent.Children.Add(Empty("Largest root folders and files will appear after an actual scan.", 190));
        selectedContent.Children.Add(MutedText("No scan results. Selecting a drive only reads volume capacity metadata."));
        typesContent.Children.Add(Empty("Classification is planned for SA3.", 125));
        categoriesContent.Children.Add(Empty("Category breakdown is planned for SA3.", 125));
        errorsContent.Children.Add(MutedText("No scan errors reported."));
        state.Text = "NO SCAN STARTED";
    }

    private void RenderActual(StorageScanResult actual)
    {
        var root = actual.Roots.FirstOrDefault();
        if (root is null)
        {
            mapContent.Children.Add(MutedText("No filesystem root returned."));
            return;
        }

        // Persist drill-down across demo toggles, but always initialize a new
        // scan at its root (StartScanAsync resets focus and selection).
        focusDirectory ??= root;
        selectedNode ??= focusDirectory;

        mapContent.Children.Add(new TextBlock
        {
            Text = actual.Cancelled
                ? "PARTIAL RESULTS  /  CANCELLED SCAN"
                : "REAL MEASURED DATA  /  CLICK TO SELECT",
            Foreground = Cyan, FontSize = 11, FontWeight = FontWeight.SemiBold
        });

        var filter = new WrapPanel { ItemSpacing = 8, LineSpacing = 7 };
        filter.Children.Add(new TextBlock
        {
            Text = "MIN FILE SIZE ON MAP",
            Foreground = Cyan, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11, FontWeight = FontWeight.SemiBold
        });
        filter.Children.Add(minimumPreset);
        filter.Children.Add(customAmount);
        filter.Children.Add(customUnit);
        filter.Children.Add(applyCustom);
        mapContent.Children.Add(filter);
        mapContent.Children.Add(mapStatistics);

        var tools = new WrapPanel { ItemSpacing = 8, LineSpacing = 7 };
        tools.Children.Add(upFolder);
        tools.Children.Add(zoomFolder);
        mapContent.Children.Add(tools);
        mapContent.Children.Add(breadcrumbs);
        mapContent.Children.Add(treemap);
        mapContent.Children.Add(hoverDetails);
        mapContent.Children.Add(MutedText(
            "Every included positive-size file receives its own rectangle; smaller than a screen pixel may not be distinguishable until zoomed. " +
            "Click a file to select; double-click it to enter its containing folder. Folder navigation is also available through the list and breadcrumbs. " +
            "Minimum size changes only the map, not the scan."));

        RefreshActualNavigation();

        typesContent.Children.Add(MutedText(
            "File extension and type breakdown is scheduled for SA3."));
        categoriesContent.Children.Add(MutedText(
            "Classification, sorting/searching and Windows Explorer actions are scheduled for SA3."));
        errorsContent.Children.Add(MutedText(
            $"{actual.InaccessibleEntries:N0} inaccessible · {actual.FilteredEntries:N0} filtered · " +
            $"{actual.ReparseEntries:N0} reparse entries skipped.\n" +
            "Filtered or inaccessible bytes are not included in treemap totals."));
        foreach (var item in actual.ErrorSamples.Take(8))
            errorsContent.Children.Add(MutedText(item));
        if (actual.InaccessibleEntries > actual.ErrorSamples.Count)
            errorsContent.Children.Add(MutedText("Additional errors were counted but not listed."));
    }

    private void ApplyMinimumToMap()
    {
        if (closed || previewActive || result is null || focusDirectory is null)
            return;
        mapStatistics.Text = "Applying minimum file size to map…";
        treemap.ShowDirectory(focusDirectory, appliedMinimumBytes);
    }

    private void ApplyCustomMinimum()
    {
        if (!double.TryParse(customAmount.Text, NumberStyles.Float,
                CultureInfo.CurrentCulture, out double value)
            || !double.IsFinite(value) || value < 0)
        {
            mapStatistics.Text = "Enter a non-negative number for the custom minimum size.";
            return;
        }

        int power = customUnit.SelectedIndex + 1;
        if (power is < 1 or > 3)
        {
            mapStatistics.Text = "Select KiB, MiB or GiB.";
            return;
        }

        double bytes = value * Math.Pow(1024, power);
        if (!double.IsFinite(bytes) || bytes >= long.MaxValue)
        {
            mapStatistics.Text = "Custom size is too large.";
            return;
        }

        appliedMinimumBytes = (long)Math.Ceiling(bytes);
        ApplyMinimumToMap();
    }

    private void NavigateToFolder(StorageNode directory)
    {
        if (result is null || previewActive || !directory.IsDirectory)
            return;
        focusDirectory = directory;
        selectedNode = directory;
        RefreshActualNavigation();
    }

    private void SelectActualNode(StorageNode node)
    {
        if (result is null || previewActive || focusDirectory is null)
            return;
        selectedNode = node;
        treemap.SelectedNode = node;
        RefreshLargestItems();
        RefreshSelectedDetails();
        RefreshNavigationButtons();
    }

    private void RefreshActualNavigation()
    {
        if (result is null || focusDirectory is null)
            return;

        mapStatistics.Text = "Building file-level map…";
        treemap.ShowDirectory(focusDirectory, appliedMinimumBytes);
        treemap.SelectedNode = selectedNode;
        // Compute top-15 only when changing folder. Tile selection should
        // never repeat an O(number-of-children) traversal.
        focusedTopEntries = TreemapLayout.CreateEntries(focusDirectory, 16)
            .Where(x => x.Node is not null).Take(15).ToArray();

        var ancestors = new List<StorageNode>();
        for (var n = focusDirectory; n is not null; n = n.Parent)
            ancestors.Add(n);
        ancestors.Reverse();

        breadcrumbs.Children.Clear();
        // Keep deep paths readable: always show the root and last six levels.
        if (ancestors.Count > 7)
        {
            AddCrumb(ancestors[0]);
            breadcrumbs.Children.Add(MutedText("…"));
            foreach (var ancestor in ancestors.Skip(ancestors.Count - 6))
                AddCrumb(ancestor);
        }
        else
            foreach (var ancestor in ancestors)
                AddCrumb(ancestor);

        hoverDetails.Text = "Hover over a file rectangle for its measured size and path.";
        RefreshLargestItems();
        RefreshSelectedDetails();
        RefreshNavigationButtons();
    }

    private void AddCrumb(StorageNode node)
    {
        var captured = node;
        var button = BtxUiKit.CreateButton(ShortPath(node.Name),
            ReferenceEquals(node, focusDirectory)
                ? BtxUiKit.ButtonKind.Primary : BtxUiKit.ButtonKind.Quiet);
        button.Padding = new Thickness(9, 5);
        button.Click += (_, _) => NavigateToFolder(captured);
        breadcrumbs.Children.Add(button);
    }

    private void RefreshNavigationButtons()
    {
        upFolder.IsEnabled = focusDirectory?.Parent is not null;
        zoomFolder.IsEnabled = selectedNode is { IsDirectory: true }
            && !ReferenceEquals(selectedNode, focusDirectory);
    }

    private void RefreshLargestItems()
    {
        itemsContent.Children.Clear();
        if (focusDirectory is null)
            return;

        // The top-15 set is cached per displayed directory; a selection
        // repaint only recreates these few buttons, never rescans children.
        if (focusedTopEntries.Count == 0)
        {
            itemsContent.Children.Add(MutedText("No files with positive measured size here."));
            return;
        }

        itemsContent.Children.Add(new TextBlock
        {
            Text = "LARGEST 15 ENTRIES  /  CLICK TO SELECT",
            Foreground = Cyan, FontSize = 11
        });
        foreach (var item in focusedTopEntries)
        {
            var node = item.Node!;
            var button = BtxUiKit.CreateButton(
                $"{(node.IsDirectory ? "▣" : "□")}  {node.Name}  ·  {FormatBytes(node.LogicalBytes)}",
                ReferenceEquals(node, selectedNode)
                    ? BtxUiKit.ButtonKind.Primary : BtxUiKit.ButtonKind.Quiet);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => SelectActualNode(node);
            itemsContent.Children.Add(button);
        }
    }

    private void RefreshSelectedDetails()
    {
        selectedContent.Children.Clear();
        if (selectedNode is not { } node || focusDirectory is not { } focus)
        {
            selectedContent.Children.Add(MutedText("Select a tile or a listed item."));
            return;
        }

        selectedContent.Children.Add(new TextBlock
        {
            Text = node.Name,
            Foreground = Cyan,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        double share = focus.LogicalBytes > 0
            ? node.LogicalBytes * 100.0 / focus.LogicalBytes : 0;
        selectedContent.Children.Add(MutedText(
            $"{(node.IsDirectory ? "Folder" : "File")} · {FormatBytes(node.LogicalBytes)}\n" +
            $"Share of scanned folder: {share:N2}%\n" +
            $"Included files: {node.FileCount:N0}\n" +
            $"Included subdirectories: {node.SubdirectoryCount:N0}\n" +
            $"Path: {node.GetFullPath()}"));
        selectedContent.Children.Add(MutedText(
            "Included, readable logical size only. SA3 will add Explorer/copy-path actions."));
    }

    private void RenderDemo()
    {
        mapContent.Children.Add(new TextBlock
        {
            Text = "DEMO ONLY  /  Fictional layout, not your drive",
            Foreground = Cyan, FontSize = 11
        });
        var map = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,2*,1.5*"),
            RowDefinitions = new RowDefinitions("180,115"),
            ColumnSpacing = 4, RowSpacing = 4
        };
        for (int i = 0; i < Demo.Length; ++i)
        {
            var item = Demo[i];
            var button = new Button
            {
                Background = Brush.Parse(item.Color),
                Foreground = Brush.Parse("#FFFFFF"),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush.Parse("#91D5E6"),
                Padding = new Thickness(12),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = $"{item.Name}\n{item.Size}",
                    FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap
                }
            };
            button.Click += (_, _) => SelectDemo(item);
            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            map.Children.Add(button);
        }
        mapContent.Children.Add(map);
        itemsContent.Children.Add(new TextBlock
        {
            Text = "NAME                                       SIZE           SHARE",
            Foreground = Cyan, FontSize = 11
        });
        foreach (var item in Demo)
        {
            var button = new Button
            {
                Content = $"{item.Name,-24}  {item.Size,10}   {item.Weight:0}%",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brush.Parse("#122E3C"),
                Foreground = Brush.Parse("#D9F6FF")
            };
            button.Click += (_, _) => SelectDemo(item);
            itemsContent.Children.Add(button);
        }
        selectedContent.Children.Add(MutedText("Select a demo tile. No real paths or Explorer actions are available."));
        typesContent.Children.Add(new TextBlock
        {
            Text = "DEMO DISTRIBUTION", Foreground = Cyan, FontSize = 11
        });
        AddBar(typesContent, "Applications", 55, "#8649D5");
        AddBar(typesContent, "Documents", 24, "#287DCF");
        AddBar(typesContent, "Media", 13, "#149B83");
        AddBar(typesContent, "Other", 8, "#D98A32");
        foreach (var item in Demo.Take(5))
            AddBar(categoriesContent, item.Name, item.Weight, item.Color);
    }

    private void SelectDemo(DemoItem item)
    {
        selectedContent.Children.Clear();
        selectedContent.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = Cyan
        });
        selectedContent.Children.Add(MutedText(
            $"Size: {item.Size}\nDemo share: {item.Weight:0}%\nPath: DEMO ONLY\nExplorer and file actions become available for genuine entries in SA3."));
    }

    private static void AddBar(StackPanel parent, string name, double percent, string color)
    {
        parent.Children.Add(new TextBlock
        {
            Text = $"{name}  /  {percent:0}%",
            Foreground = Brush.Parse("#E1F2F7"), FontSize = 12
        });
        parent.Children.Add(new ProgressBar
        {
            Minimum = 0, Maximum = 100, Value = percent, Height = 8,
            Foreground = Brush.Parse(color), Background = Brush.Parse("#183646")
        });
    }

    private static Control Empty(string message, double height) => new Border
    {
        Height = height, Background = Brush.Parse("#091C29"),
        CornerRadius = new CornerRadius(7),
        BorderBrush = Brush.Parse("#204658"),
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = message, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16)
        }
    };

    private static Control Section(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = title, Foreground = Cyan, FontSize = 12,
            FontWeight = FontWeight.SemiBold, LetterSpacing = 1
        });
        stack.Children.Add(content);
        return Card(stack);
    }

    private static BtxTechFrame Card(Control content, bool emphasized = false) =>
        BtxUiKit.CreateTechPanel(content, emphasized);
}
