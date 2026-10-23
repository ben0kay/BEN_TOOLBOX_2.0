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
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

// Shared native read-only inspector shell for File Size and Godot Resource Checker.
public sealed class InspectorWindow : Window
{
    private readonly string kind;
    private readonly TextBox folder = new() { PlaceholderText = "Choose folder..." };
    private readonly Button scan = new() { Content = "SCAN / REFRESH" }, cancel = new() { Content = "CANCEL", IsEnabled = false };
    private readonly TextBlock status = new() { Text = "Ready", TextWrapping = TextWrapping.Wrap };
    private readonly ListBox rows = new() { MinHeight = 340 };
    private CancellationTokenSource? running;
    public InspectorWindow(string kind)
    {
        this.kind=kind;
        Title=kind=="size" ? "File Size Inspector / Native" : "Resource Checker / Native";
        Width=1050;Height=760;MinWidth=780;MinHeight=550;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        var container=new StackPanel { Margin=new Thickness(22),Spacing=14 };
        container.Children.Add(new TextBlock
        {
            Text=kind=="size" ? "FILE SIZE INSPECTOR   /   NATIVE" : "RESOURCE CHECKER   /   NATIVE",
            FontSize=23,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#8BE5F6")
        });
        container.Children.Add(new TextBlock { Text=kind=="size"
            ? "Read-only file size ranking. Generated and version-control directories are skipped."
            : "Read-only static check for missing Godot res:// references. Dynamic references may not be detected.",
            Foreground=Brush.Parse("#A8BED0"),TextWrapping=TextWrapping.Wrap });
        var picker=new Grid { ColumnDefinitions=new ColumnDefinitions("*,120"),ColumnSpacing=10 };
        picker.Children.Add(folder);
        var browse=new Button { Content="BROWSE" };
        browse.Click+=async (_,_) =>
        {
            var found=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple=false });
            if(found.Count>0 && found[0].Path.IsFile) folder.Text=found[0].Path.LocalPath;
        };
        Grid.SetColumn(browse,1);picker.Children.Add(browse);container.Children.Add(picker);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12 };
        actions.Children.Add(scan);actions.Children.Add(cancel);container.Children.Add(actions);
        container.Children.Add(status);
        rows.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<InspectorRow>((item,_) =>
            new TextBlock { Text=item.Display,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(7),
                Foreground=Brush.Parse("#BCEAF5") },true);
        container.Children.Add(rows);
        Content=new ScrollViewer { Content=container };
        scan.Click+=async (_,_)=>await Scan();
        cancel.Click+=(_,_)=>running?.Cancel();
        Closed+=(_,_)=>running?.Cancel();
    }
    private async Task Scan()
    {
        if(running!=null)return;
        var root=folder.Text?.Trim().Trim('"') ?? "";
        running=new CancellationTokenSource();
        scan.IsEnabled=false;cancel.IsEnabled=true;rows.ItemsSource=null;status.Text="Scanning...";
        try
        {
            List<InspectorRow> result=kind=="size"
                ? await InspectorServices.LargestFilesAsync(root,200,running.Token)
                : await InspectorServices.CheckResourcesAsync(root,running.Token);
            rows.ItemsSource=result;
            status.Text=$"Scan complete / {result.Count:N0} results";
        }
        catch(OperationCanceledException){status.Text="Scan cancelled.";}
        catch(Exception e){status.Text="Scan error: "+e.Message;}
        finally{running.Dispose();running=null;scan.IsEnabled=true;cancel.IsEnabled=false;}
    }
}
