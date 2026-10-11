using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public sealed class WorldInspectorWindow : Window
{
    private readonly string kind;
    private readonly TextBox folder=new() { Watermark="Select a Godot project folder containing project.godot..." };
    private readonly TextBox search=new() { Watermark="Filter results..." };
    private readonly ComboBox biome=new() { MinWidth=210 };
    private readonly ComboBox filter=new() { MinWidth=135,ItemsSource=new[]{"All","Trees","Plants","Grass","Rocks","Enemies","Unregistered"},SelectedIndex=0 };
    private readonly TextBlock details=new() { TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#B4CDDE") };
    private readonly TextBlock status=new() { Text="Ready. Read-only saved configuration." };
    private readonly ListBox rows=new() { MinHeight=280 };
    private readonly Button scan=new() { Content="REFRESH" },locate=new() { Content="LOCATE SELECTED" };
    private readonly StackPanel biomeControls=new() { Orientation=Orientation.Horizontal,Spacing=10 };
    private CancellationTokenSource? running;
    private List<LightingSetting> lightingRows=new();
    private List<PopulationRow> populationRows=new();
    private List<BiomeOption> biomeOptions=new();
    private BiomeInspectionService? engine;
    public WorldInspectorWindow(string kind)
    {
        this.kind=kind;
        Title=kind=="lighting"?"Lighting & Shadow Inspector / Native":"Biome Population Inspector / Native";
        Width=1160;Height=800;MinWidth=820;MinHeight=600;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        var panel=new StackPanel { Margin=new Thickness(22),Spacing=13 };
        panel.Children.Add(new TextBlock { Text=kind=="lighting"?"LIGHTING & SHADOW INSPECTOR  /  NATIVE":"BIOME POPULATION INSPECTOR  /  NATIVE",
            FontSize=23,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#86E5F6") });
        panel.Children.Add(new TextBlock { Text="Saved Godot resource values only. This inspector does not write files or resolve live gameplay state.",
            TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#9BB7CF") });
        var selector=new Grid { ColumnDefinitions=new ColumnDefinitions("*,115"),ColumnSpacing=10 };
        selector.Children.Add(folder);
        var browse=new Button { Content="BROWSE" };
        browse.Click+=async (_,_)=>{
            var pick=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple=false });
            if(pick.Count>0&&pick[0].Path.IsFile)folder.Text=pick[0].Path.LocalPath;
        };
        Grid.SetColumn(browse,1);selector.Children.Add(browse);panel.Children.Add(selector);
        var actions=new WrapPanel { ItemSpacing=12,LineSpacing=8 };
        actions.Children.Add(scan);actions.Children.Add(locate);actions.Children.Add(search);
        panel.Children.Add(actions);
        if(kind=="biome")
        {
            biomeControls.Children.Add(new TextBlock { Text="BIOME",VerticalAlignment=VerticalAlignment.Center });
            biomeControls.Children.Add(biome);
            biomeControls.Children.Add(new TextBlock { Text="SHOW",VerticalAlignment=VerticalAlignment.Center });
            biomeControls.Children.Add(filter);
            panel.Children.Add(biomeControls);
        }
        panel.Children.Add(details);panel.Children.Add(status);
        rows.ItemTemplate=kind=="lighting"
            ?new Avalonia.Controls.Templates.FuncDataTemplate<LightingSetting>((x,_)=>
                new TextBlock { Text=x.Display,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(7),Foreground=Brush.Parse("#B6E7F6") },true)
            :new Avalonia.Controls.Templates.FuncDataTemplate<PopulationRow>((x,_)=>
                new TextBlock { Text=x.Display,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(7),Foreground=Brush.Parse("#B6E7F6") },true);
        panel.Children.Add(rows);
        Content=new ScrollViewer { Content=panel };
        scan.Click+=async (_,_)=>await Scan();
        biome.SelectionChanged+=async (_,_)=>{ if(kind=="biome"&&biome.SelectedItem is BiomeOption b&&running==null)await InspectBiome(b); };
        filter.SelectionChanged+=(_,_)=>Filter();
        search.TextChanged+=(_,_)=>Filter();
        locate.Click+=(_,_)=>Locate();
        Closed+=(_,_)=>running?.Cancel();
    }
    private async Task Scan()
    {
        if(running!=null)return;
        var root=folder.Text?.Trim().Trim('"')??"";
        if(!File.Exists(Path.Combine(root,"project.godot"))){status.Text="Select a folder containing project.godot.";return;}
        running=new CancellationTokenSource();scan.IsEnabled=false;status.Text="Scanning...";
        try
        {
            if(kind=="lighting")
            {
                lightingRows=await LightingInspectionService.ReadAsync(root,running.Token);
                details.Text="Defaults and saved values are shown separately. Scenes can override profiles.";
                Filter();
            }
            else
            {
                engine=new BiomeInspectionService(root);
                biomeOptions=await engine.GetBiomesAsync(running.Token);
                biome.ItemsSource=null;biome.ItemsSource=biomeOptions;
                if(biomeOptions.Count>0)biome.SelectedIndex=0;
                else status.Text="No readable biomes registered in BiomeCatalog.";
            }
        }
        catch(OperationCanceledException){status.Text="Cancelled.";}
        catch(Exception ex){status.Text="Scan failed: "+ex.Message;}
        finally { running.Dispose();running=null;scan.IsEnabled=true; }
        if(kind=="biome"&&biome.SelectedItem is BiomeOption selected)await InspectBiome(selected);
    }
    private async Task InspectBiome(BiomeOption selected)
    {
        if(engine==null||running!=null)return;
        running=new CancellationTokenSource();scan.IsEnabled=false;status.Text="Inspecting selected biome...";
        try
        {
            var result=await engine.InspectAsync(selected,running.Token);
            populationRows=result.Rows;details.Text=result.Summary;Filter();
        }
        catch(OperationCanceledException){status.Text="Cancelled.";}
        catch(Exception ex){status.Text="Inspection failed: "+ex.Message;}
        finally {running.Dispose();running=null;scan.IsEnabled=true;}
    }
    private void Filter()
    {
        var q=search.Text?.Trim()??"";
        if(kind=="lighting")
        {
            var filtered=lightingRows.Where(r=>r.Display.Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();
            rows.ItemsSource=filtered;status.Text=$"{filtered.Count} of {lightingRows.Count} settings";
        }
        else
        {
            var category=filter.SelectedItem as string??"All";
            var filtered=populationRows.Where(r=>(category=="All"||r.Group==category) && r.Display.Contains(q,StringComparison.OrdinalIgnoreCase)).ToList();
            rows.ItemsSource=filtered;status.Text=$"{filtered.Count} of {populationRows.Count} entries";
        }
    }
    private void Locate()
    {
        var relative=kind=="lighting"?(rows.SelectedItem as LightingSetting)?.File:(rows.SelectedItem as PopulationRow)?.File;
        if(string.IsNullOrWhiteSpace(relative))return;
        var baseDir=folder.Text?.Trim().Trim('"')??"";
        var path=Path.GetFullPath(Path.Combine(baseDir,relative));
        if(!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
            !File.Exists(path)){status.Text="Selected file is unavailable.";return;}
        try {
            if(OperatingSystem.IsWindows())Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList={"/select,",path},UseShellExecute=true });
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute=true });
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
}
