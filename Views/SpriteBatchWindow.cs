using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using Avalonia.Input;
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

public sealed class SpriteBatchWindow : Window
{
    private readonly TextBox source=new(){Watermark="PNG source folder"};
    private readonly TextBox destination=new(){Watermark="Existing output folder"};
    private readonly TextBox prefix=new(){Text="Plant",Width=110};
    private readonly TextBox scale=new(){Text="1",Width=70};
    private readonly TextBox start=new(){Text="1",Width=70};
    private readonly TextBox digits=new(){Text="2",Width=70};
    private readonly CheckBox letters=new(){Content="Letter suffixes (01a, 01b...)"}; 
    private readonly CheckBox species=new(){Content="Species folders"}; 
    private readonly CheckBox archive=new(){Content="Archive copies of originals in destination/RAW"};
    private readonly Button preview=new(){Content="PREVIEW"},export=new(){Content="EXPORT"},cancel=new(){Content="CANCEL",IsEnabled=false};
    private readonly TextBlock status=new(){Text="Ready. Original PNG files are never changed.",TextWrapping=TextWrapping.Wrap};
    private readonly ListBox list=new(){MinHeight=290};
    private CancellationTokenSource? running;
    private string? previewKey;
    private string[]? droppedFiles;
    private readonly TextBlock inputStatus = new() { Text = "INPUT MODE: SOURCE FOLDER", Foreground = Brush.Parse("#91BCD0") };
    public SpriteBatchWindow()
    {
        Title="Sprite Batch Tool / Native";Width=1010;Height=790;MinWidth=750;MinHeight=550;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        var body=new StackPanel{Margin=new Thickness(22),Spacing=13};
        body.Children.Add(new TextBlock{Text="SPRITE BATCH TOOL / NATIVE",FontSize=24,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#86E5F6")});
        body.Children.Add(new TextBlock{Text="PNG scaling, sequential or letter naming, species folders, optional RAW backup copies. Godot .tres generation is reserved for a later pass.",TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#A9BED3")});
        var dropZone = new Border
        {
            Background = Brush.Parse("#14293D"),
            BorderBrush = Brush.Parse("#39B9D3"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "DROP PNG FILES HERE", FontSize = 18, FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush.Parse("#86E5F6") },
                    new TextBlock { Text = "Drag one or multiple PNGs from File Explorer",
                        Foreground = Brush.Parse("#A9BED3"), HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
        };
        DragDrop.SetAllowDrop(dropZone, true);
        dropZone.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        dropZone.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Where(f => f.Path.IsFile)
                .Select(f => f.Path.LocalPath)
                .Where(f => Path.GetExtension(f).Equals(".png", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
            if (files.Length == 0)
            {
                status.Text = "Drop PNG image files from File Explorer.";
                return;
            }
            droppedFiles = files;
            inputStatus.Text = $"INPUT MODE: {files.Length} DROPPED PNG FILE(S) — folder field inactive";
            status.Text = $"{files.Length} PNG(s) selected. Choose destination and click PREVIEW.";
            Invalidate();
            e.Handled = true;
        });
        body.Children.Add(dropZone);
        body.Children.Add(new TextBlock { Text = "OR CHOOSE SOURCE FOLDER", FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#91BCD0") });
        body.Children.Add(Picker(source));
        body.Children.Add(inputStatus);
        body.Children.Add(Picker(destination));
        var fields=new WrapPanel{ItemSpacing=15,LineSpacing=8};
        fields.Children.Add(Field("SCALE (0-1)",scale));fields.Children.Add(Field("PREFIX",prefix));
        fields.Children.Add(Field("START",start));fields.Children.Add(Field("DIGITS",digits));body.Children.Add(fields);
        body.Children.Add(letters);body.Children.Add(species);body.Children.Add(archive);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};
        actions.Children.Add(preview);actions.Children.Add(export);actions.Children.Add(cancel);body.Children.Add(actions);
        body.Children.Add(status);body.Children.Add(list);Content=new ScrollViewer{Content=body};
        preview.Click+=async (_,_)=>await Preview();
        export.Click+=async (_,_)=>await Export();
        cancel.Click+=(_,_)=>running?.Cancel();Closed+=(_,_)=>running?.Cancel();
        foreach(var tb in new[]{source,destination,prefix,scale,start,digits})
            tb.TextChanged+=(_,_)=>{ if (ReferenceEquals(tb, source) && droppedFiles != null)
            {
                droppedFiles = null;
                inputStatus.Text = "INPUT MODE: SOURCE FOLDER";
                status.Text = "Source folder active. Click PREVIEW.";
            }
            Invalidate(); };
        foreach(var cb in new[]{letters,species,archive})cb.IsCheckedChanged+=(_,_)=>Invalidate();
    }
    private void Invalidate(){previewKey=null;list.ItemsSource=null;}
    private static StackPanel Field(string caption,Control control)
    {
        var panel=new StackPanel{Spacing=4};
        panel.Children.Add(new TextBlock{Text=caption,FontSize=11,Foreground=Brush.Parse("#95C9D8")});
        panel.Children.Add(control);return panel;
    }
    private Grid Picker(TextBox box)
    {
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("*,110"),ColumnSpacing=10};
        grid.Children.Add(box);var button=new Button{Content="BROWSE"};
        button.Click+=async (_,_)=>{
            var selected=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{AllowMultiple=false});
            if(selected.Count>0&&selected[0].Path.IsFile)box.Text=selected[0].Path.LocalPath;
        };
        Grid.SetColumn(button,1);grid.Children.Add(button);return grid;
    }
    private SpriteBatchOptions ReadOptions()
    {
        if(!double.TryParse(scale.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var factor)||
           !int.TryParse(start.Text,out var first)||!int.TryParse(digits.Text,out var width))
            throw new ArgumentException("Enter valid scale/start/digits (use decimal point).");
        return new((source.Text??"").Trim().Trim('"'),(destination.Text??"").Trim().Trim('"'),
            (prefix.Text??"").Trim(),factor,first,width,letters.IsChecked==true,species.IsChecked==true,archive.IsChecked==true,droppedFiles?.ToArray());
    }
    private static string Key(SpriteBatchOptions o)=>System.Text.Json.JsonSerializer.Serialize(o);
    private void Busy(bool busy){preview.IsEnabled=!busy;export.IsEnabled=!busy;cancel.IsEnabled=busy;}
    private async Task Preview()
    {
        if(running!=null)return;
        running=new CancellationTokenSource();Busy(true);status.Text="Previewing...";previewKey=null;
        try
        {
            var options=ReadOptions();
            var plan=await SpriteBatchService.PlanAsync(options,running.Token);
            list.ItemsSource=plan.Select(p=>p.Display).ToArray();
            previewKey=Key(options);status.Text=$"{plan.Count} file(s) ready; no changes made.";
        }
        catch(OperationCanceledException){status.Text="Preview cancelled.";}
        catch(Exception ex){status.Text="Preview failed: "+ex.Message;}
        finally{running.Dispose();running=null;Busy(false);}
    }
    private async Task Export()
    {
        if(running!=null)return;
        SpriteBatchOptions options;
        try
        {
            options=ReadOptions();
            if(previewKey!=Key(options)){status.Text="Preview current settings before export.";return;}
        }
        catch(Exception ex){status.Text=ex.Message;return;}
        running=new CancellationTokenSource();Busy(true);status.Text="Exporting...";
        try
        {
            var count=await SpriteBatchService.ExportAsync(options,new Progress<string>(s=>status.Text=s),running.Token);
            status.Text=$"Completed {count} PNG exports. Sources preserved.";previewKey=null;
        }
        catch(OperationCanceledException){status.Text="Cancelled; created files rolled back.";previewKey=null;}
        catch(Exception ex){status.Text="Export stopped: "+ex.Message;previewKey=null;}
        finally{running.Dispose();running=null;Busy(false);}
    }
}
