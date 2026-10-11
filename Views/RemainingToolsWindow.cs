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
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;

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
    public RemainingToolsWindow(string kind)
    {
        this.kind=kind;Title=kind=="artwork"?"Artwork Design Prompter / Native":kind=="usage"?"Asset Usage Finder / Native":"Folder Backup / Native";
        Width=1050;Height=800;MinWidth=760;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        var body=new StackPanel{Margin=new Thickness(22),Spacing=12};
        body.Children.Add(new TextBlock{Text=Title.ToUpperInvariant(),FontSize=23,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#86E5F6")});
        body.Children.Add(new TextBlock{Text=kind=="artwork"?"Build reusable artwork prompts for your isometric game.":kind=="usage"?"Find static direct and indirect Godot asset references. Dynamic references may not be discoverable.":"Create a new, non-overwriting copy of a folder, or preview selected files. Source files are never removed.",TextWrapping=TextWrapping.Wrap});
        if(kind=="artwork")
        {
            body.Children.Add(Field("CATEGORY",category));
            foreach(var pair in new[]{("SUBJECT",prefix),("SHAPE",shape),("MATERIAL",material),("DETAILS",details),("COLOURS",colours),("NOTES",notes)})
                body.Children.Add(Field(pair.Item1,pair.Item2));
            body.Children.Add(transparent);body.Children.Add(noShadow);
            run.Content="GENERATE PROMPT";second.Content="COPY PROMPT";
            body.Children.Add(output);
        }
        else
        {
            body.Children.Add(Picker(project,true));
            body.Children.Add(Picker(target,kind=="backup"));
            if(kind=="backup")
            {
                body.Children.Add(Field("EXCLUDE FOLDER NAMES (COMMA SEPARATED)",extra));
                body.Children.Add(Field("EXCLUDE EXTENSIONS (COMMA SEPARATED)",ext));
                run.Content="CREATE BACKUP";second.Content="PREVIEW";
            }
            else {run.Content="SEARCH USAGE";second.Content="COPY RESULTS";}
            body.Children.Add(results);
        }
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};
        bar.Children.Add(run);bar.Children.Add(second);bar.Children.Add(cancel);body.Children.Add(bar);body.Children.Add(status);
        Content=new ScrollViewer{Content=body};
        run.Click+=async(_,_)=>await Primary();
        second.Click+=async(_,_)=>await Secondary();
        cancel.Click+=(_,_)=>running?.Cancel();
        Closed+=(_,_)=>running?.Cancel();
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
        finally{running.Dispose();running=null;run.IsEnabled=true;second.IsEnabled=true;cancel.IsEnabled=false;}
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
        try
        {
            var plan=await FolderBackupService.PreviewAsync(BackupSettings(),running.Token);
            results.ItemsSource=plan.Files.Take(400).Select(f=>$"{f.Bytes/(1024.0*1024.0):N2} MiB  {f.Relative}").ToArray();
            status.Text=plan.Summary+" (first 400 paths shown)";
        }
        catch(OperationCanceledException){status.Text="Preview cancelled.";}
        catch(Exception ex){status.Text="Preview failed: "+ex.Message;}
        finally{running.Dispose();running=null;run.IsEnabled=true;second.IsEnabled=true;cancel.IsEnabled=false;}
    }
}
