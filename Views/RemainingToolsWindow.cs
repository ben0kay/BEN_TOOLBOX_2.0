using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;
using BEN_TOOLBOX_2_0.Shared.UI;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class RemainingToolsWindow : Window
{
    private readonly string kind;
    private readonly TextBox project=new(){Watermark="Project or source folder"};
    private readonly TextBox target=new(){Watermark="Asset path or destination folder"};
    private readonly TextBox prefix=new(){Text="Alien fern"};
    private readonly TextBox shape=new(){Text="Natural, distinct silhouette"};
    private readonly TextBox material=new(){Text="Weathered organic textures"};
    private readonly TextBox details=new(){Text="Biome-adapted detail; readable at sprite size"};
    private readonly TextBox colours=new(){Text="Muted greens with pale cyan accents"};
    private readonly TextBox notes=new(){Watermark="Optional creative notes",AcceptsReturn=true,Height=62};
    private readonly TextBox extra=new(){Text=".git, .godot, bin, obj, node_modules"};
    private readonly TextBox ext=new(){Text=".tmp, .log"};
    private readonly ComboBox category=new(){ItemsSource=ArtworkPromptService.Categories,SelectedIndex=0,MinWidth=180};
    private readonly CheckBox transparent=new(){Content="Transparent background",IsChecked=true};
    private readonly CheckBox noShadow=new(){Content="No cast shadow",IsChecked=true};
    private readonly TextBlock status=new(){Text="Ready",TextWrapping=TextWrapping.Wrap};
    private readonly ListBox results=new(){MinHeight=280};
    private readonly TextBox output=new(){AcceptsReturn=true,IsReadOnly=true,Height=220,TextWrapping=TextWrapping.Wrap};
    private readonly Button run=new(){Content="RUN"},second=new(){Content="PREVIEW"},cancel=new(){Content="CANCEL",IsEnabled=false};
    private CancellationTokenSource? running;
    private ProgressBar? backupActivity;
    public RemainingToolsWindow(string kind)
    {
        this.kind = kind;
        Title = kind == "artwork" ? "Artwork Design Prompter / Native"
            : kind == "usage" ? "Asset Usage Finder / Native"
            : "Folder Backup / Native";
        Width = 1050;
        Height = 800;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse(kind == "backup" ? "#060F1C" : "#091522");
        Foreground = Brush.Parse("#E8F4FF");

        // The new BTX presentation is strictly backup-only. The two other
        // tool modes retain their original Fluent layout and visual behaviour.
        if (kind == "backup")
            BuildBackupLayout();
        else
            BuildLegacyLayout();

        // One set of existing event handlers for all three modes.
        run.Click += async (_, _) => await Primary();
        second.Click += async (_, _) => await Secondary();
        cancel.Click += (_, _) => running?.Cancel();
        Closed += (_, _) => running?.Cancel();
    }

    private void BuildBackupLayout()
    {
        // Opt-in styling; nothing here touches Asset Usage Finder controls.
        foreach (var input in new[] { project, target, extra, ext })
            input.Classes.Add("btx-input");

        BtxUiKit.ApplyButtonStyle(run, BtxUiKit.ButtonKind.Primary);
        BtxUiKit.ApplyButtonStyle(second, BtxUiKit.ButtonKind.Secondary);
        BtxUiKit.ApplyButtonStyle(cancel, BtxUiKit.ButtonKind.Danger);
        run.Content = "CREATE BACKUP";
        second.Content = "PREVIEW";
        cancel.Content = "CANCEL";

        results.Classes.Add("btx-results");
        results.MinHeight = 180;
        results.Height = 235;

        status.Text = "Ready · Choose a source and destination folder.";
        status.Classes.Add("btx-muted");
        status.FontSize = 13;

        // Fixed header, independently scrolling content and visible action/status
        // footer: even a short window can scroll its settings without losing Cancel.
        var root = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14
        };

        var header = BtxUiKit.CreateHeader(
            "FILE TOOLBOX  /  BACKUP UTILITY",
            "FOLDER BACKUP",
            "Create a separate, non-overwriting copy of your folder, or preview the selected files. Source files are never removed.");
        root.Children.Add(BtxUiKit.CreateTechPanel(header, emphasized: true));

        var paths = new StackPanel { Spacing = 14 };
        paths.Children.Add(BtxUiKit.CreateField(
            "SOURCE FOLDER",
            Picker(project, folder: true),
            "Choose the folder to copy."));
        paths.Children.Add(BtxUiKit.CreateField(
            "DESTINATION FOLDER",
            Picker(target, folder: true),
            "The backup service creates a new copy without overwriting existing backups."));

        var exclusions = new StackPanel { Spacing = 14 };
        exclusions.Children.Add(BtxUiKit.CreateField(
            "EXCLUDE FOLDER NAMES (COMMA SEPARATED)",
            extra,
            "Skip matching folder names, such as caches and build outputs."));
        exclusions.Children.Add(BtxUiKit.CreateField(
            "EXCLUDE EXTENSIONS (COMMA SEPARATED)",
            ext,
            "Files with these extensions are omitted from backup and preview."));

        var setupRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,0.85*"),
            ColumnSpacing = 14
        };
        setupRow.Children.Add(BackupSection(
            "01  /  SOURCE & DESTINATION", paths));
        var exclusionsPanel = BackupSection("02  /  FILTERS & EXCLUSIONS", exclusions);
        Grid.SetColumn(exclusionsPanel, 1);
        setupRow.Children.Add(exclusionsPanel);

        var previewContent = new StackPanel { Spacing = 10 };
        var previewHint = new TextBlock
        {
            Text = "Run Preview to list eligible paths (up to 400 shown). No file scan runs when this window opens.",
            TextWrapping = TextWrapping.Wrap
        };
        previewHint.Classes.Add("btx-muted");
        previewContent.Children.Add(previewHint);
        previewContent.Children.Add(results);

        var main = new StackPanel { Spacing = 14 };
        main.Children.Add(setupRow);
        main.Children.Add(BackupSection("03  /  PREVIEW & RESULTS", previewContent));

        var scrolling = new ScrollViewer
        {
            Content = main,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scrolling, 1);
        root.Children.Add(scrolling);

        var activity = new StackPanel { Spacing = 7, MinWidth = 120 };
        var activityTitle = new TextBlock { Text = "ACTIVITY  /  STATUS" };
        activityTitle.Classes.Add("btx-kicker");
        activity.Children.Add(activityTitle);
        activity.Children.Add(status);

        // The number of files to process is not known before Preview/Backup.
        // Indeterminate animation is only visible while a real job is running.
        backupActivity = new ProgressBar
        {
            IsIndeterminate = true,
            IsVisible = false,
            Height = 7
        };
        backupActivity.Classes.Add("btx-progress");
        activity.Children.Add(backupActivity);

        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        actions.Children.Add(run);
        actions.Children.Add(second);
        actions.Children.Add(cancel);

        var bottomContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16
        };
        bottomContent.Children.Add(activity);
        Grid.SetColumn(actions, 1);
        bottomContent.Children.Add(actions);

        var bottom = BtxUiKit.CreateTechPanel(bottomContent);
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        Content = root;
    }

    private static BtxTechFrame BackupSection(string heading, Control content)
    {
        var section = new StackPanel { Spacing = 12 };
        var title = new TextBlock { Text = heading };
        title.Classes.Add("btx-section-heading");
        section.Children.Add(title);
        section.Children.Add(content);
        return BtxUiKit.CreateTechPanel(section);
    }

    // Unmodified legacy mode visuals (Artwork Prompter / Asset Usage Finder).
    private void BuildLegacyLayout()
    {
        var body = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = Title.ToUpperInvariant(), FontSize = 23, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#86E5F6") });
        body.Children.Add(new TextBlock
        {
            Text = kind == "artwork"
                ? "Build reusable artwork prompts for your isometric game."
                : "Find static direct and indirect Godot asset references. Dynamic references may not be discoverable.",
            TextWrapping = TextWrapping.Wrap
        });

        if (kind == "artwork")
        {
            body.Children.Add(Field("CATEGORY", category));
            foreach (var pair in new[] { ("SUBJECT", prefix), ("SHAPE", shape), ("MATERIAL", material), ("DETAILS", details), ("COLOURS", colours), ("NOTES", notes) })
                body.Children.Add(Field(pair.Item1, pair.Item2));
            body.Children.Add(transparent);
            body.Children.Add(noShadow);
            run.Content = "GENERATE PROMPT";
            second.Content = "COPY PROMPT";
            body.Children.Add(output);
        }
        else
        {
            body.Children.Add(Picker(project, true));
            body.Children.Add(Picker(target, false));
            run.Content = "SEARCH USAGE";
            second.Content = "COPY RESULTS";
            body.Children.Add(results);
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        bar.Children.Add(run);
        bar.Children.Add(second);
        bar.Children.Add(cancel);
        body.Children.Add(bar);
        body.Children.Add(status);
        Content = new ScrollViewer { Content = body };
    }

    private void SetBackupActivity(bool busy)
    {
        if (kind == "backup" && backupActivity is not null)
            backupActivity.IsVisible = busy;
    }

    private static StackPanel Field(string name,Control control)
    {
        var panel=new StackPanel{Spacing=4};
        panel.Children.Add(new TextBlock{Text=name,FontSize=11,Foreground=Brush.Parse("#9DCBDD")});
        panel.Children.Add(control);return panel;
    }
    private Grid Picker(TextBox box,bool folder)
    {
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("*,110"),ColumnSpacing=10};
        grid.Children.Add(box);var browse=new Button{Content="BROWSE"};
        if (kind == "backup") BtxUiKit.ApplyButtonStyle(browse);
        browse.Click+=async(_,_)=>{
            if(folder)
            {
                var choices=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{AllowMultiple=false});
                if(choices.Count>0&&choices[0].Path.IsFile)box.Text=choices[0].Path.LocalPath;
            }
            else
            {
                var choices=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{AllowMultiple=false});
                if(choices.Count>0&&choices[0].Path.IsFile)box.Text=choices[0].Path.LocalPath;
            }
        };
        Grid.SetColumn(browse,1);grid.Children.Add(browse);return grid;
    }
    private BackupOptions BackupSettings()=>new(project.Text?.Trim()??"",target.Text?.Trim()??"",extra.Text??"",ext.Text??"");
    private async Task Primary()
    {
        if(running!=null)return;
        if(kind=="artwork")
        {
            output.Text=ArtworkPromptService.Compose(category.SelectedItem?.ToString()??"Plant",prefix.Text??"",shape.Text??"",material.Text??"",details.Text??"",colours.Text??"",notes.Text??"",1,transparent.IsChecked==true,noShadow.IsChecked==true);
            status.Text="Prompt generated.";return;
        }
        running=new CancellationTokenSource();run.IsEnabled=false;second.IsEnabled=false;cancel.IsEnabled=true;status.Text="Working...";
        SetBackupActivity(true);
        try
        {
            if(kind=="usage")
            {
                var rows=await AssetUsageService.SearchAsync(project.Text??"",target.Text??"",running.Token);
                results.ItemsSource=rows.Select(x=>x.Display).ToArray();
                status.Text=$"{rows.Count} direct/indirect matches. No matches does not prove unused.";
            }
            else
            {
                var done=await FolderBackupService.RunAsync(BackupSettings(),new Progress<string>(s=>status.Text=s),running.Token);
                status.Text="Backup created: "+done;
                results.ItemsSource=new[]{"Backup output: "+done,"Open the Summary.txt file to check completion and errors."};
            }
        }
        catch(OperationCanceledException){status.Text="Cancelled; an incomplete backup may remain in the destination.";}
        catch(Exception ex){status.Text="Stopped: "+ex.Message;}
        finally{running.Dispose();running=null;run.IsEnabled=true;second.IsEnabled=true;cancel.IsEnabled=false;SetBackupActivity(false);}
    }
    private async Task Secondary()
    {
        if(kind=="artwork")
        {
            if(!string.IsNullOrWhiteSpace(output.Text))
            {try{await Clipboard!.SetTextAsync(output.Text);status.Text="Copied to clipboard.";}catch(Exception ex){status.Text=ex.Message;}}
            return;
        }
        if(kind=="usage")
        {
            var text=string.Join(Environment.NewLine,results.ItemsSource?.Cast<string>()??Array.Empty<string>());
            if(text.Length>0)
            {try{await Clipboard!.SetTextAsync(text);status.Text="Results copied.";}catch(Exception ex){status.Text=ex.Message;}}
            return;
        }
        if(running!=null)return;
        running=new CancellationTokenSource();run.IsEnabled=false;second.IsEnabled=false;cancel.IsEnabled=true;
        SetBackupActivity(true);
        try
        {
            var plan=await FolderBackupService.PreviewAsync(BackupSettings(),running.Token);
            results.ItemsSource=plan.Files.Take(400).Select(f=>$"{f.Bytes/(1024.0*1024.0):N2} MiB  {f.Relative}").ToArray();
            status.Text=plan.Summary+" (first 400 paths shown)";
        }
        catch(OperationCanceledException){status.Text="Preview cancelled.";}
        catch(Exception ex){status.Text="Preview failed: "+ex.Message;}
        finally{running.Dispose();running=null;run.IsEnabled=true;second.IsEnabled=true;cancel.IsEnabled=false;SetBackupActivity(false);}
    }
}
