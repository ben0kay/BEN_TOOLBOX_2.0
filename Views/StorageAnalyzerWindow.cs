using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BEN_TOOLBOX_2_0.Shared.UI;

namespace BEN_TOOLBOX_2_0.Views;

/// <summary>
/// SA0 visual shell. Reading drive capacity metadata is not a scan.
/// Demo entries are synthetic and never treated as disk results.
/// </summary>
public sealed class StorageAnalyzerWindow : Window
{
    private static readonly IBrush Cyan = Brush.Parse("#53E1E9");
    private static readonly IBrush Muted = Brush.Parse("#98B8C8");
    private readonly ComboBox drivePicker = new() { MinWidth = 200 };
    private readonly TextBlock state = new() { Text = "NO SCAN STARTED", Foreground = Cyan, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock driveDetails = new() { Text = "Choose a drive. No files will be read.", Foreground = Muted, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel mapContent = new() { Spacing = 9 };
    private readonly StackPanel itemsContent = new() { Spacing = 5 };
    private readonly StackPanel selectedContent = new() { Spacing = 8 };
    private readonly StackPanel typesContent = new() { Spacing = 8 };
    private readonly StackPanel categoriesContent = new() { Spacing = 8 };
    private readonly TextBlock selectedTitle = new() { Text = "Nothing selected", FontSize = 18, FontWeight = FontWeight.SemiBold };
    private bool previewActive;

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

        var page = new StackPanel { Margin = new Thickness(22), Spacing = 16 };
        page.Children.Add(new TextBlock { Text = "FILES  /  BTX STORAGE ANALYZER  /  SA0", Foreground = Cyan, FontSize = 12, LetterSpacing = 1.5 });
        page.Children.Add(new TextBlock { Text = "STORAGE ANALYZER", FontSize = 30, FontWeight = FontWeight.Bold });
        page.Children.Add(new TextBlock { Text = "Visualize disk usage · Identify large folders · Open locations in Explorer (SA3)", Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap });

        var toolbar = new WrapPanel { ItemSpacing = 12, LineSpacing = 10 };
        toolbar.Children.Add(new TextBlock { Text = "TARGET DRIVE", Foreground = Cyan, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
        PopulateDrives();
        drivePicker.SelectionChanged += (_, _) => UpdateDriveDetails();
        toolbar.Children.Add(drivePicker);
        var scan = BtxUiKit.CreateButton("SCAN SELECTED DRIVE  /  SA1");
        scan.IsEnabled = false;
        toolbar.Children.Add(scan);
        var demo = BtxUiKit.CreateButton("SHOW DESIGN PREVIEW", BtxUiKit.ButtonKind.Primary);
        demo.Click += (_, _) => { previewActive = !previewActive; demo.Content = previewActive ? "CLEAR DESIGN PREVIEW" : "SHOW DESIGN PREVIEW"; Render(); };
        toolbar.Children.Add(demo);
        toolbar.Children.Add(state);
        page.Children.Add(Card(toolbar));

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("245,*,285"), ColumnSpacing = 13 };
        var left = new StackPanel { Spacing = 13 };
        left.Children.Add(Section("DRIVE SELECTION", driveDetails));
        left.Children.Add(Section("SCAN SETTINGS  /  PLANNED", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Single-drive selection in SA0. Multi-drive scanning is planned.", TextWrapping = TextWrapping.Wrap, Foreground = Muted },
                new CheckBox { Content = "Include subfolders", IsChecked = true, IsEnabled = false },
                new CheckBox { Content = "Follow junctions", IsChecked = false, IsEnabled = false },
                new CheckBox { Content = "Include hidden files", IsChecked = true, IsEnabled = false }
            }
        }));
        left.Children.Add(Section("EXCLUSIONS  /  SA1", new TextBlock
        {
            Text = "Folder paths, patterns and presets will be applied before enumeration. No exclusions are active yet.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap
        }));
        layout.Children.Add(left);

        var middle = new StackPanel { Spacing = 13 };
        middle.Children.Add(Section("DISK USAGE TREEMAP", mapContent));
        middle.Children.Add(Section("LARGEST ITEMS", itemsContent));
        Grid.SetColumn(middle, 1); layout.Children.Add(middle);

        var right = new StackPanel { Spacing = 13 };
        selectedContent.Children.Add(selectedTitle);
        right.Children.Add(Section("SELECTED ITEM", selectedContent));
        right.Children.Add(Section("FILE TYPE BREAKDOWN", typesContent));
        right.Children.Add(Section("LARGEST CATEGORIES", categoriesContent));
        Grid.SetColumn(right, 2); layout.Children.Add(right);

        page.Children.Add(layout);
        page.Children.Add(new TextBlock
        {
            Text = "SA0: layout preview only. No filesystem enumeration, no background scan, no cleanup, and no file actions. Preview figures are fictitious.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        Content = new ScrollViewer { Content = page };
        Render();
    }

    private void PopulateDrives()
    {
        // DriveInfo.GetDrives lists volumes; it does not traverse directories.
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

    private void UpdateDriveDetails()
    {
        var name = drivePicker.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name) || name == "Select a drive...")
        {
            driveDetails.Text = "Choose a drive. No files will be read.";
            return;
        }
        try
        {
            var drive = new DriveInfo(name);
            driveDetails.Text = drive.IsReady
                ? $"{drive.Name}  /  {drive.DriveFormat}\nCapacity: {FormatBytes(drive.TotalSize)}\nFree: {FormatBytes(drive.AvailableFreeSpace)}\nNo scan started."
                : $"{name}\nDrive not ready. No scan started.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            driveDetails.Text = $"{name}\nCapacity unavailable. No scan started.";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024d / 1024d:N1} MiB";
        return $"{bytes / 1024d / 1024d / 1024d:N1} GiB";
    }

    private void Render()
    {
        state.Text = previewActive ? "DEMO DATA  /  NOT SCANNED" : "NO SCAN STARTED";
        mapContent.Children.Clear();
        itemsContent.Children.Clear();
        selectedContent.Children.Clear();
        selectedContent.Children.Add(selectedTitle);
        typesContent.Children.Clear();
        categoriesContent.Children.Clear();

        if (!previewActive)
        {
            mapContent.Children.Add(Empty("Treemap will appear after you scan a selected drive.", 315));
            itemsContent.Children.Add(Empty("Largest files and folders will appear here.", 190));
            selectedTitle.Text = "Nothing selected";
            selectedContent.Children.Add(Empty("Select a file or folder to inspect its details.", 115));
            typesContent.Children.Add(Empty("No file types analysed.", 125));
            categoriesContent.Children.Add(Empty("No categories analysed.", 125));
            return;
        }

        mapContent.Children.Add(new TextBlock { Text = "DEMO ONLY  /  Fictional layout, not your drive", Foreground = Cyan, FontSize = 11 });
        var map = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*,1.5*"), RowDefinitions = new RowDefinitions("180,115"), ColumnSpacing = 4, RowSpacing = 4 };
        for (var i = 0; i < Demo.Length; i++)
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
                Content = new TextBlock { Text = $"{item.Name}\n{item.Size}", FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap }
            };
            button.Click += (_, _) => SelectDemo(item);
            Grid.SetColumn(button, i % 3); Grid.SetRow(button, i / 3);
            map.Children.Add(button);
        }
        mapContent.Children.Add(map);
        itemsContent.Children.Add(new TextBlock { Text = "NAME                                       SIZE           SHARE", Foreground = Cyan, FontSize = 11 });
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
        selectedTitle.Text = "Select a demo tile";
        selectedContent.Children.Add(new TextBlock { Text = "Preview items are synthetic; Explorer actions are unavailable.", TextWrapping = TextWrapping.Wrap, Foreground = Muted });
        typesContent.Children.Add(new TextBlock { Text = "DEMO DISTRIBUTION", Foreground = Cyan, FontSize = 11 });
        AddBar(typesContent, "Applications", 55, "#8649D5");
        AddBar(typesContent, "Documents", 24, "#287DCF");
        AddBar(typesContent, "Media", 13, "#149B83");
        AddBar(typesContent, "Other", 8, "#D98A32");
        foreach (var item in Demo.Take(5)) AddBar(categoriesContent, item.Name, item.Weight, item.Color);
    }

    private void SelectDemo(DemoItem item)
    {
        selectedContent.Children.Clear();
        selectedContent.Children.Add(new TextBlock { Text = item.Name, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = Cyan });
        selectedContent.Children.Add(new TextBlock { Text = $"Size: {item.Size}\nDemo share: {item.Weight:0}%\nPath: DEMO ONLY", TextWrapping = TextWrapping.Wrap });
        selectedContent.Children.Add(new TextBlock { Text = "Open in Explorer / Copy Path / Rescan Folder will be enabled for genuine scan entries in SA3.", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
    }

    private static void AddBar(StackPanel parent, string name, double percent, string color)
    {
        parent.Children.Add(new TextBlock { Text = $"{name}  /  {percent:0}%", Foreground = Brush.Parse("#E1F2F7"), FontSize = 12 });
        parent.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = percent, Height = 8, Foreground = Brush.Parse(color), Background = Brush.Parse("#183646") });
    }

    private static Control Empty(string message, double height) => new Border
    {
        Height = height, Background = Brush.Parse("#091C29"), CornerRadius = new CornerRadius(7),
        BorderBrush = Brush.Parse("#204658"), BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = message, Foreground = Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16) }
    };

    private static Border Section(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = title, Foreground = Cyan, FontSize = 12, FontWeight = FontWeight.SemiBold, LetterSpacing = 1 });
        stack.Children.Add(content);
        return Card(stack);
    }

    private static Border Card(Control content) => BtxUiKit.CreatePanel(content);
}
