using System;
using System.Collections.Generic;
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

namespace BEN_TOOLBOX_2_0.Views;

/// <summary>
/// SA1 manual metadata scanner. SA0 fictional design preview remains separate;
/// real treemap geometry and Explorer actions are scheduled for SA2/SA3.
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
        activityBar.Classes.Add("btx-progress");
        includeHidden.Classes.Add("btx-check");
        developerPreset.Classes.Add("btx-check");

        var page = new StackPanel { Margin = new Thickness(22), Spacing = 16 };
        page.Children.Add(new TextBlock
        {
            Text = "FILES  /  BTX STORAGE ANALYZER  /  SA1",
            Foreground = Cyan, FontSize = 12, LetterSpacing = 1.5
        });
        page.Children.Add(new TextBlock
        {
            Text = "STORAGE ANALYZER", FontSize = 30, FontWeight = FontWeight.Bold
        });
        page.Children.Add(new TextBlock
        {
            Text = "Scan filesystem metadata · Filter folders before scanning · Explore disk usage (treemap in SA2)",
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
        middle.Children.Add(Section("DISK USAGE TREEMAP  /  SA2", mapContent));
        middle.Children.Add(Section("LARGEST ROOT ITEMS", itemsContent));
        Grid.SetColumn(middle, 1);
        layout.Children.Add(middle);

        var right = new StackPanel { Spacing = 13 };
        right.Children.Add(Section("SCAN DETAILS", selectedContent));
        right.Children.Add(Section("FILE TYPE BREAKDOWN  /  SA3", typesContent));
        right.Children.Add(Section("LARGEST CATEGORIES  /  SA3", categoriesContent));
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);

        page.Children.Add(layout);
        page.Children.Add(new TextBlock
        {
            Text = "SA1: read-only filesystem metadata. Logical file sizes may differ from disk used space. Access-denied and excluded entries are not measured. No deletion, Explorer actions or automatic scans.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        Content = new ScrollViewer { Content = page };

        drivePicker.SelectionChanged += (_, _) =>
        {
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

        mapContent.Children.Add(Empty("Real treemap visualization is coming in SA2. Select a drive and click Scan to collect actual file sizes.", 315));
        itemsContent.Children.Add(Empty("Largest root folders and files will appear after an actual scan.", 190));
        selectedContent.Children.Add(MutedText("No scan results. Selecting a drive only reads volume capacity metadata."));
        typesContent.Children.Add(Empty("Classification is planned for SA3.", 125));
        categoriesContent.Children.Add(Empty("Category breakdown is planned for SA3.", 125));
        errorsContent.Children.Add(MutedText("No scan errors reported."));
        state.Text = "NO SCAN STARTED";
    }

    private void RenderActual(StorageScanResult actual)
    {
        string outcome = actual.Cancelled ? "PARTIAL / CANCELLED" : "REAL SCAN DATA";
        mapContent.Children.Add(new TextBlock
        {
            Text = outcome, Foreground = Cyan, FontSize = 12, FontWeight = FontWeight.Bold
        });
        mapContent.Children.Add(Empty(
            $"Measured {FormatBytes(actual.LogicalBytes)} across {actual.Files:N0} files.\nThe interactive proportional treemap is the next phase (SA2).\nScan results are retained in a hierarchical file model.", 240));

        var firstRoot = actual.Roots.FirstOrDefault();
        if (firstRoot is not null)
        {
            itemsContent.Children.Add(new TextBlock
            {
                Text = "ROOT ENTRIES  /  LARGEST FIRST (MAX 15)", Foreground = Cyan, FontSize = 11
            });
            foreach (var node in firstRoot.Children.OrderByDescending(x => x.LogicalBytes).Take(15))
            {
                string mark = node.IsDirectory ? "▣" : "□";
                itemsContent.Children.Add(new TextBlock
                {
                    Text = $"{mark}  {node.Name}  —  {FormatBytes(node.LogicalBytes)}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#DFEFFF"), FontSize = 12
                });
            }
            if (firstRoot.Children.Count == 0)
                itemsContent.Children.Add(MutedText("No readable, included items were found at the root."));
            selectedContent.Children.Add(MutedText(
                $"Root: {firstRoot.Name}\nIncluded logical size: {FormatBytes(firstRoot.LogicalBytes)}\nDiscovered files: {actual.Files:N0}\nDiscovered folders (including root): {actual.Directories:N0}\nElapsed: {actual.Elapsed.TotalSeconds:N1}s\nAllocated-on-disk usage is not measured in SA1."));
        }

        typesContent.Children.Add(MutedText("Real type breakdown is scheduled for SA3."));
        categoriesContent.Children.Add(MutedText("Real categorized summaries and Explorer actions are scheduled for SA3."));

        errorsContent.Children.Add(MutedText(
            $"{actual.InaccessibleEntries:N0} inaccessible entries · {actual.FilteredEntries:N0} filtered · {actual.ReparseEntries:N0} reparse entries skipped.\nFiltered or inaccessible bytes are not included in the measured total."));
        foreach (var item in actual.ErrorSamples.Take(8))
            errorsContent.Children.Add(MutedText(item));
        if (actual.InaccessibleEntries > actual.ErrorSamples.Count)
            errorsContent.Children.Add(MutedText("Additional errors were counted but not listed."));
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
