using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BEN_TOOLBOX_2_0.Services;
using Microsoft.VisualBasic.FileIO;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class DuplicateFinderWindow : Window
{
    private readonly DuplicateScanService engine = new();
    private readonly TextBox a = new() { Watermark="Source A path" }, b = new() { Watermark="Destination B path" };
    private readonly CheckBox subA = new() { Content="Include subfolders in A" },
        subB = new() { Content="Include subfolders in B",IsChecked=true },
        verify = new() { Content="Verify SHA-256 contents",IsChecked=true };
    private readonly Button scan = new() { Content="SCAN" }, cancel = new() { Content="CANCEL",IsEnabled=false },
        export = new() { Content="EXPORT CSV",IsEnabled=false },
        delete = new() { Content="RECYCLE SELECTED A",IsEnabled=false },
        deleteAll = new() { Content="RECYCLE ALL VERIFIED A",IsEnabled=false };
    private readonly TextBlock status = new() { Text="Ready / Nothing is deleted during scanning.",TextWrapping=TextWrapping.Wrap };
    private readonly ListBox results = new() { MinHeight=280 };
    private readonly Image preview = new() { Height=200,Stretch=Stretch.Uniform };
    private readonly TextBlock previewTitle = new() { Text="Select a result to preview",TextWrapping=TextWrapping.Wrap };
    private List<DuplicateMatch> matches = new();
    private CancellationTokenSource? running;
    private string scannedA="",scannedB="";
    public DuplicateFinderWindow()
    {
        Title="Duplicate File Finder / Avalonia";Width=1120;Height=820;MinWidth=820;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        var panel=new StackPanel { Margin=new Thickness(22),Spacing=12 };
        panel.Children.Add(new TextBlock { Text="DUPLICATE FILE FINDER  /  NATIVE",FontSize=23,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#8BE4F6") });
        panel.Children.Add(new TextBlock { Text="SOURCE FOLDER A   /   Files eligible for recycling" });
        panel.Children.Add(PathRow(a));panel.Children.Add(subA);
        panel.Children.Add(new TextBlock { Text="DESTINATION FOLDER B   /   Never deleted" });
        panel.Children.Add(PathRow(b));panel.Children.Add(subB);panel.Children.Add(verify);
        var buttons=new WrapPanel { ItemSpacing=10,LineSpacing=8 };
        foreach (var button in new[] { scan,cancel,export,delete,deleteAll })
        {
            button.Padding=new Thickness(12,8);
            button.Background=Brush.Parse(button==delete || button==deleteAll ? "#653347" : "#18526C");
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);panel.Children.Add(status);
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("*,240"),ColumnSpacing=14 };
        results.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<DuplicateMatch>((item,_) =>
            new TextBlock { Text=item.Description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6),
                Foreground=Brush.Parse(item.Identical ? "#8FE3BF" : "#E4CD94") },true);
        grid.Children.Add(results);
        var right=new StackPanel { Spacing=12 };
        right.Children.Add(new TextBlock { Text="IMAGE PREVIEW",Foreground=Brush.Parse("#8BE4F6") });
        right.Children.Add(preview);right.Children.Add(previewTitle);
        Grid.SetColumn(right,1);grid.Children.Add(right);panel.Children.Add(grid);
        panel.Children.Add(new TextBlock { Text="Deletion requires SHA-256 identity, a second verification, and confirmation. Files go to Windows Recycle Bin.",FontSize=12,TextWrapping=TextWrapping.Wrap });
        Content=new ScrollViewer { Content=panel };
        scan.Click += async (_,_) => await Scan();
        cancel.Click += (_,_) => running?.Cancel();
        export.Click += async (_,_) => await Export();
        delete.Click += async (_,_) => await Recycle(false);
        deleteAll.Click += async (_,_) => await Recycle(true);
        results.SelectionChanged += (_,_) => { ShowPreview();UpdateButtons(); };
        a.TextChanged += (_,_) => Invalidate();
        b.TextChanged += (_,_) => Invalidate();
        subA.IsCheckedChanged += (_,_) => Invalidate();
        subB.IsCheckedChanged += (_,_) => Invalidate();
        verify.IsCheckedChanged += (_,_) => Invalidate();
        Closed += (_,_) => running?.Cancel();
    }
    private Control PathRow(TextBox textbox)
    {
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("*,110"),ColumnSpacing=8 };
        grid.Children.Add(textbox);
        var button=new Button { Content="BROWSE" };
        button.Click += async (_,_) =>
        {
            var selected=await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { AllowMultiple=false });
            if (selected.Count>0 && selected[0].Path.IsFile) textbox.Text=selected[0].Path.LocalPath;
        };
        Grid.SetColumn(button,1);grid.Children.Add(button);return grid;
    }
    private void Invalidate()
    {
        running?.Cancel();matches.Clear();results.ItemsSource=null;scannedA=scannedB="";
        preview.Source=null;export.IsEnabled=false;UpdateButtons();
    }
    private void UpdateButtons()
    {
        delete.IsEnabled=running==null && results.SelectedItem is DuplicateMatch { Identical:true };
        deleteAll.IsEnabled=running==null && matches.Any(x=>x.Identical);
    }
    private async Task Scan()
    {
        if (running!=null) return;
        var source=a.Text?.Trim().Trim('"') ?? "";var target=b.Text?.Trim().Trim('"') ?? "";
        try { DuplicateScanService.CheckRoots(source,target); }
        catch(Exception ex) { await Alert(ex.Message);return; }
        running=new CancellationTokenSource();scan.IsEnabled=false;cancel.IsEnabled=true;UpdateButtons();
        status.Text="Scanning in background...";
        try
        {
            var result=await engine.ScanAsync(source,target,subA.IsChecked==true,subB.IsChecked==true,
                verify.IsChecked==true,running.Token);
            matches=result.Rows;scannedA=source;scannedB=target;results.ItemsSource=matches;
            export.IsEnabled=matches.Count>0;
            status.Text=$"Source: {result.SourceCount:N0} / Destination: {result.DestinationCount:N0} / Matches: {matches.Count:N0} / Identical: {matches.Count(x=>x.Identical):N0}";
        }
        catch(OperationCanceledException) { status.Text="Scan cancelled."; }
        catch(Exception ex) { status.Text="Scan failed.";await Alert(ex.Message); }
        finally { running.Dispose();running=null;scan.IsEnabled=true;cancel.IsEnabled=false;UpdateButtons(); }
    }
    private void ShowPreview()
    {
        preview.Source=null;
        if (results.SelectedItem is not DuplicateMatch row) return;
        previewTitle.Text=Path.GetFileName(row.Source);
        if (!new[] { ".png",".jpg",".jpeg",".bmp",".gif",".webp" }.Contains(Path.GetExtension(row.Source).ToLowerInvariant())) return;
        try { using var stream=File.OpenRead(row.Source);preview.Source=new Bitmap(stream); }
        catch { previewTitle.Text += " (no preview)"; }
    }
    private async Task Export()
    {
        var picked=await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        { SuggestedFileName="DuplicateFileFinder_Results.csv",Title="Save CSV" });
        if (picked==null || !picked.Path.IsFile) return;
        static string Csv(object? value) => "\"" + (value?.ToString() ?? "").Replace("\"","\"\"") + "\"";
        var lines=new List<string> { "Status,Source,Destination,BytesA,BytesB" };
        lines.AddRange(matches.Select(m=>string.Join(",",new[]{Csv(m.Status),Csv(m.Source),Csv(m.Destination),Csv(m.SizeA),Csv(m.SizeB)})));
        try { await File.WriteAllLinesAsync(picked.Path.LocalPath,lines,Encoding.UTF8);status.Text="CSV saved."; }
        catch(Exception ex) { await Alert(ex.Message); }
    }
    private async Task Recycle(bool all)
    {
        if (!OperatingSystem.IsWindows()) { await Alert("Recycle Bin operations require Windows.");return; }
        var selected=all ? matches.Where(m=>m.Identical) :
            results.SelectedItem is DuplicateMatch m && m.Identical ? new[] { m } : Array.Empty<DuplicateMatch>();
        var unique=selected.GroupBy(m=>m.Source,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
        if (unique.Length==0 || !await Confirm($"Move {unique.Length} source A file(s) to Recycle Bin? Destination B is protected. Each match will be reverified.")) return;
        scan.IsEnabled=false;delete.IsEnabled=false;deleteAll.IsEnabled=false;
        int count=0;var errors=new List<string>();
        try
        {
            foreach(var match in unique)
            {
                try
                {
                    if (!await Task.Run(()=>DuplicateScanService.VerifyAgain(match,scannedA,scannedB)))
                        throw new IOException("Files are no longer verified identical.");
                    FileSystem.DeleteFile(match.Source,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException);
                    count++;
                }
                catch(Exception ex) { errors.Add(Path.GetFileName(match.Source)+": "+ex.Message); }
            }
            matches=matches.Where(m=>File.Exists(m.Source)).ToList();
            results.ItemsSource=null;results.ItemsSource=matches;
            export.IsEnabled=matches.Count>0;
            status.Text=$"Recycled {count} file(s). Failures: {errors.Count}.";
            if(errors.Count>0) await Alert(string.Join("\n",errors.Take(10)));
        }
        finally { scan.IsEnabled=true;UpdateButtons(); }
    }
    private async Task<bool> Confirm(string message)
    {
        var popup=new Window { Title="Confirm deletion",Width=540,Height=220,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        var panel=new StackPanel { Margin=new Thickness(20),Spacing=16 };
        panel.Children.Add(new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap });
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=12 };
        var no=new Button { Content="CANCEL" };var yes=new Button { Content="YES, RECYCLE SOURCE FILES" };
        no.Click+=(_,_)=>popup.Close(false);yes.Click+=(_,_)=>popup.Close(true);
        actions.Children.Add(no);actions.Children.Add(yes);panel.Children.Add(actions);popup.Content=panel;
        return await popup.ShowDialog<bool>(this);
    }
    private async Task Alert(string message)
    {
        var popup=new Window { Title="Duplicate Finder",Width=530,Height=210,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Content=new TextBlock { Text=message,Margin=new Thickness(22),TextWrapping=TextWrapping.Wrap } };
        await popup.ShowDialog(this);
    }
}
