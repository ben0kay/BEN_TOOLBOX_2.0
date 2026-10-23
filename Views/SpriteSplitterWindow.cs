using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class SpriteSplitterWindow : Window
{
    private readonly Image sheet = new() { Stretch=Stretch.Uniform };
    private readonly Canvas overlays = new() { Background=Brushes.Transparent };
    private readonly TextBlock info = new() { Text="Open a transparent PNG sprite sheet." };
    private readonly TextBox destination = new() { Watermark="Export folder" }, prefix = new() { Text="Sprite" };
    private readonly NumericUpDown threshold = new() { Minimum=0,Maximum=254,Value=12,Increment=1 },
        gap = new() { Minimum=1,Maximum=1000,Value=3,Increment=1 },
        padding = new() { Minimum=0,Maximum=512,Value=2,Increment=1 };
    private readonly ComboBox canvasMode = new() { ItemsSource=new[] {"Individual crop","Equal rectangle","Equal square"},SelectedIndex=0 };
    private readonly ListBox regionList = new() { MinHeight=110,MaxHeight=180 };
    private readonly List<int> cuts = new();
    private List<SpriteRegion>? cachedRegions;
    private string? source;
    private int imageWidth,imageHeight;
    private int? selectedCut;
    private bool dragging;
    public SpriteSplitterWindow()
    {
        Title="Sprite Splitter / Native Avalonia";Width=1180;Height=850;MinWidth=850;MinHeight=600;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        var root=new StackPanel { Margin=new Thickness(22),Spacing=12 };
        root.Children.Add(new TextBlock { Text="SPRITE SPLITTER   /   NATIVE MODULE",FontSize=23,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#86E5F6") });
        var actions=new WrapPanel { ItemSpacing=9,LineSpacing=9 };
        AddButton(actions,"OPEN PNG",async ()=>await Open());
        AddButton(actions,"SUGGEST CUTS",()=>{ if(source!=null) { var found=SpriteSplitService.Detect(source,(byte)(threshold.Value??0),(int)(gap.Value??1));cuts.Clear();cuts.AddRange(found.Cuts);Refresh(); } });
        AddButton(actions,"REMOVE SELECTED",()=>{ if(selectedCut.HasValue) { cuts.Remove(selectedCut.Value);selectedCut=null;Refresh(); } });
        AddButton(actions,"CLEAR CUTS",()=>{cuts.Clear();selectedCut=null;Refresh();});
        root.Children.Add(actions);
        var controls=new WrapPanel { ItemSpacing=15,LineSpacing=8 };
        controls.Children.Add(Labeled("Alpha threshold",threshold));
        controls.Children.Add(Labeled("Minimum gap",gap));
        controls.Children.Add(Labeled("Padding",padding));
        controls.Children.Add(Labeled("Canvas",canvasMode));
        root.Children.Add(controls);
        root.Children.Add(new TextBlock {
            Text="Canvas changes the exported PNG size; the source-sheet preview does not resize.",
            FontSize=11,Foreground=Brush.Parse("#8CAFC6"),TextWrapping=TextWrapping.Wrap
        });
        var frame=new Border { Height=390,BorderThickness=new Thickness(1),BorderBrush=Brush.Parse("#2A7089"),Background=Brush.Parse("#122132"),ClipToBounds=true };
        var imageGrid=new Grid();
        imageGrid.Children.Add(sheet);imageGrid.Children.Add(overlays);
        frame.Child=imageGrid;root.Children.Add(frame);
        root.Children.Add(new TextBlock { Text="Click image to add/select a cut; drag a line to move it. Cuts are vertical. Red line = selected.",FontSize=12,Foreground=Brush.Parse("#9ABCD0") });
        root.Children.Add(info);
        root.Children.Add(regionList);
        var paths=new Grid { ColumnDefinitions=new ColumnDefinitions("*,125"),ColumnSpacing=10 };
        paths.Children.Add(destination);
        var browse=new Button { Content="BROWSE" };browse.Click+=async (_,_)=>await ChooseExport();
        Grid.SetColumn(browse,1);paths.Children.Add(browse);
        root.Children.Add(paths);
        root.Children.Add(Labeled("Filename prefix",prefix));
        AddButton(root,"EXPORT PNGs",async ()=>await Export());
        Content=new ScrollViewer { Content=root };
        overlays.PointerPressed+=PointerDown;overlays.PointerMoved+=PointerMove;overlays.PointerReleased+=PointerUp;
        overlays.SizeChanged+=(_,_)=>RenderCuts();
        threshold.ValueChanged+=(_,_)=>Refresh();
        canvasMode.SelectionChanged+=(_,_)=>Refresh(recalculateRegions:false);
        padding.ValueChanged+=(_,_)=>Refresh(recalculateRegions:false);
    }
    private static Control Labeled(string title,Control control)
    {
        var column=new StackPanel { Spacing=4,MinWidth=130 };
        column.Children.Add(new TextBlock { Text=title,FontSize=11,Foreground=Brush.Parse("#A8CADB") });
        column.Children.Add(control);return column;
    }
    private static void AddButton(Panel parent,string label,Action work)
    {
        var button=new Button { Content=label,Padding=new Thickness(13,9),Background=Brush.Parse("#17465E") };
        button.Click+=(_,_)=>work();parent.Children.Add(button);
    }
    private static void AddButton(Panel parent,string label,Func<Task> work)
    {
        var button=new Button { Content=label,Padding=new Thickness(13,9),Background=Brush.Parse("#17465E") };
        button.Click+=async (_,_)=>await work();parent.Children.Add(button);
    }
    private async Task Open()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            AllowMultiple=false,Title="Open PNG",FileTypeFilter=new[] { new FilePickerFileType("PNG") { Patterns=new[] {"*.png"} } } });
        if(files.Count==0 || !files[0].Path.IsFile)return;
        try
        {
            string path=files[0].Path.LocalPath;
            var loaded=SpriteSplitService.Detect(path,(byte)(threshold.Value??0),(int)(gap.Value??1));
            using(var stream=File.OpenRead(path))sheet.Source=new Bitmap(stream);
            source=path;imageWidth=loaded.Width;imageHeight=loaded.Height;
            cuts.Clear();cuts.AddRange(loaded.Cuts);selectedCut=null;Refresh();
        }
        catch(Exception e){await Alert(e.Message);}
    }
    private async Task ChooseExport()
    {
        var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple=false,Title="Export directory" });
        if(folders.Count>0 && folders[0].Path.IsFile)destination.Text=folders[0].Path.LocalPath;
    }
    private (double left,double top,double width,double height) ImageRect()
    {
        var w=overlays.Bounds.Width;var h=overlays.Bounds.Height;
        if(imageWidth==0 || imageHeight==0 || w<=0 || h<=0)return (0,0,0,0);
        var scale=Math.Min(w/imageWidth,h/imageHeight);
        var iw=imageWidth*scale;var ih=imageHeight*scale;
        return ((w-iw)/2,(h-ih)/2,iw,ih);
    }
    private void RenderCuts()
    {
        overlays.Children.Clear();
        var r=ImageRect();if(r.width<=0)return;
        foreach(var cut in cuts)
        {
            var line=new Border { Width=2,Height=r.height,Background=Brush.Parse(cut==selectedCut?"#FF6E81":"#59E7FB"),IsHitTestVisible=false };
            Canvas.SetLeft(line,r.left+cut*r.width/imageWidth-1);
            Canvas.SetTop(line,r.top);overlays.Children.Add(line);
        }
    }
    private int? PixelX(PointerEventArgs e)
    {
        var r=ImageRect();var p=e.GetPosition(overlays);
        if(p.X<r.left || p.X>r.left+r.width || p.Y<r.top || p.Y>r.top+r.height || r.width<=0)return null;
        return Math.Clamp((int)((p.X-r.left)*imageWidth/r.width),1,imageWidth-1);
    }
    private void PointerDown(object? sender,PointerPressedEventArgs e)
    {
        if(source==null || !e.GetCurrentPoint(overlays).Properties.IsLeftButtonPressed)return;
        var x=PixelX(e);if(!x.HasValue)return;
        int nearest=cuts.Count==0?-1:cuts.OrderBy(v=>Math.Abs(v-x.Value)).First();
        var r=ImageRect();int tolerance=(int)Math.Max(3,8*imageWidth/r.width);
        if(nearest>=0 && Math.Abs(nearest-x.Value)<=tolerance)selectedCut=nearest;
        else {cuts.Add(x.Value);selectedCut=x.Value;}
        dragging=true;e.Pointer.Capture(overlays);Refresh();
    }
    private void PointerMove(object? sender,PointerEventArgs e)
    {
        if(!dragging || !selectedCut.HasValue)return;
        var x=PixelX(e);if(!x.HasValue)return;
        cuts.Remove(selectedCut.Value);cuts.Add(x.Value);selectedCut=x.Value;RenderCuts();
    }
    private void PointerUp(object? sender,PointerReleasedEventArgs e)
    {
        dragging=false;e.Pointer.Capture(null);cuts.Sort();Refresh();
    }
    private void Refresh(bool recalculateRegions = true)
    {
        RenderCuts();
        if(source==null) { cachedRegions=null; return; }
        try
        {
            // Canvas/padding edits are presentation-only; don't rescan/decode the PNG.
            if(recalculateRegions || cachedRegions==null)
                cachedRegions=SpriteSplitService.Regions(source,cuts,(byte)(threshold.Value??0));
            var regions=cachedRegions;
            int p=(int)(padding.Value??0);
            int mode=canvasMode.SelectedIndex;
            string modeName=mode switch {
                1=>"EQUAL RECTANGLE",
                2=>"EQUAL SQUARE",
                _=>"INDIVIDUAL CROP"
            };
            regionList.ItemsSource=regions.Select((r,i)=>
            {
                var size=SpriteSplitService.ExportCanvasSize(regions,r,p,mode);
                return $"Sprite {i+1:D2}  /  Art {r.Width} x {r.Height}  /  Export {size.Width} x {size.Height} px  /  x={r.X} y={r.Y}";
            }).ToArray();

            string outputDetail = regions.Count==0
                ? "No visible sprites"
                : mode==0
                    ? "each sprite uses its own canvas"
                    : $"ALL EXPORTS {SpriteSplitService.ExportCanvasSize(regions,regions[0],p,mode).Width} x {SpriteSplitService.ExportCanvasSize(regions,regions[0],p,mode).Height} px";
            info.Text=$"{Path.GetFileName(source)}  /  {imageWidth} x {imageHeight}  /  {cuts.Count} cuts  /  {regions.Count} sprites  /  {modeName}: {outputDetail}";
        }
        catch(Exception e){info.Text="Preview error: "+e.Message;}
    }
    private async Task Export()
    {
        if(source==null){await Alert("Open a PNG first.");return;}
        try
        {
            // Snapshot ALL Avalonia control values and mutable cut positions on the UI thread.
            // Only plain data is passed to Task.Run; controls must never be read from a worker.
            string inputFile=source;
            string exportFolder=destination.Text??"";
            string filePrefix=prefix.Text??"";
            int[] exportCuts=cuts.ToArray();
            byte alphaThreshold=(byte)(threshold.Value??0);
            int exportPadding=(int)(padding.Value??0);
            int exportMode=canvasMode.SelectedIndex;

            var files=await Task.Run(()=>SpriteSplitService.Export(
                inputFile,exportFolder,filePrefix,exportCuts,alphaThreshold,exportPadding,exportMode));

            info.Text=$"Exported {files.Count} PNGs. Original source preserved.";
            await Alert($"Exported {files.Count} PNGs to {exportFolder}");
        }
        catch(Exception e){await Alert(e.Message);}
    }
    private async Task Alert(string message)
    {
        var dialog=new Window { Title="Sprite Splitter",Width=540,Height=200,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Content=new TextBlock { Text=message,Margin=new Thickness(20),TextWrapping=TextWrapping.Wrap } };
        await dialog.ShowDialog(this);
    }
}
