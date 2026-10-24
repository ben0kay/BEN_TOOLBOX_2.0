using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Models;
using BEN_TOOLBOX_2_0.Services;
using BEN_TOOLBOX_2_0.Views.Controls;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class SeasonalSpriteStudioWindow : Window
{
    private SeasonalMaskProject? project;
    private readonly MaskHistory history=new();
    private readonly SeasonalMaskCanvas canvas=new();
    private readonly TextBlock status=new(){Text="Load a final-sized transparent PNG. Source artwork is always read-only.",TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock sourceInfo=new(){Text="No source loaded",TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock pickInfo=new(){Text="Detector: hue selection",TextWrapping=TextWrapping.Wrap};
    private readonly ComboBox view=new(){ItemsSource=new[]{"Original","Mask Overlay","Automatic Mask","Protected Trunk","Effective Mask","Seasonal Preview","Leafless removal","Generated branches"},SelectedIndex=1};
    private readonly ComboBox brush=new(){ItemsSource=new[]{"Pan","Add Foliage","Remove Foliage","Protect Trunk","Unprotect","Pick Similar Colour","Mark Branch Root","Mark Branch Tips"},SelectedIndex=0};
    private readonly ComboBox season=new(){ItemsSource=new[]{"Spring","Summer","Autumn","Winter (leafy)"},SelectedIndex=1};
    private readonly CheckBox foliageVisible=new(){Content="Show foliage overlay",IsChecked=true},protectionVisible=new(){Content="Show trunk protection overlay",IsChecked=true};
    private readonly Slider hue=S(0,360,110),tolerance=S(1,180,55),saturation=S(0,1,0.18),dark=S(0,1,0.03),alpha=S(0,255,8,true),canopy=S(0,1,0),softness=S(0,1,0.2),grow=S(-4,4,0,true),islands=S(0,16,0,true),colourDistance=S(0.01,1,0.3);
    private readonly Slider radius=S(1,64,10,true),strength=S(0.01,1,1),overlay=S(0,1,0.55),effect=S(0,1,1),autumn=S(0,1,0.65),snow=S(0,1,0.4),patch=S(1,128,12,true);
    private readonly TextBox presetName=new(){PlaceholderText="Seasonal preset name"};
    private readonly Slider branchThickness=S(1,48,12),branchCurve=S(0,1,0.2),branchForks=S(0,4,2,true),branchSeed=S(0,9999,17,true),branchCutoff=S(1,255,32,true),branchVariation=S(0,0.5,0.18);
    private readonly TextBox branchColour=new(){Text="#73503A"};
    private readonly CheckBox preserveBrown=new(){Content="Preserve detected brown wood",IsChecked=true};
    private readonly TextBlock branchInfo=new(){Text="No connection points",TextWrapping=TextWrapping.Wrap};
    private readonly Button cancel=new(){Content="CANCEL",IsEnabled=false};
    private readonly StackPanel left=new(){Spacing=9},right=new(){Spacing=10};
    private readonly Dictionary<long,MaskDelta> stroke=new();
    private Point? lastPoint;
    private int? strokeMode;
    private Bitmap? bitmap;
    private CancellationTokenSource? running;
    private readonly CancellationTokenSource lifetime=new();
    private bool closed,busy,pumping,dirty,detectDirty,loadingControls,unsaved,confirmingClose,lifetimeDisposed;
    private string? pickedColour;
    private long lastPreview;
    private string? projectFile;
    private FoliageDetection? detectedSettings;

    public SeasonalSpriteStudioWindow()
    {
        Title="Seasonal Sprite Studio";Width=1440;Height=940;MinWidth=1100;MinHeight=650;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid { Margin=new Thickness(18),RowDefinitions=new RowDefinitions("Auto,*,Auto"),RowSpacing=12 };
        root.Children.Add(new TextBlock { Text="SEASONAL SPRITE STUDIO",FontSize=24,Foreground=Brush.Parse("#86E5F6") });
        var columns=new Grid { ColumnDefinitions=new ColumnDefinitions("220,*,340"),ColumnSpacing=12 };Grid.SetRow(columns,1);root.Children.Add(columns);
        left.Children.Add(Button("LOAD PNG",async()=>await LoadSprite()));left.Children.Add(sourceInfo);
        left.Children.Add(new TextBlock { Text="For Godot, load the final-sized sprite before generating its foliage mask.",TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#E5B968") });
        left.Children.Add(Label("VIEW",view));left.Children.Add(foliageVisible);left.Children.Add(protectionVisible);
        left.Children.Add(Button("FIT",()=>{canvas.Fit();return Task.CompletedTask;}));
        left.Children.Add(new TextBlock { Text="Wheel: zoom\nMiddle drag / Space+drag: pan\nLeft drag: active brush",TextWrapping=TextWrapping.Wrap });
        var undoRow=new WrapPanel { ItemSpacing=8 };undoRow.Children.Add(Button("UNDO",()=>{EndStroke();if(project is not null&&history.Undo(project)){unsaved=true;Request(false);}return Task.CompletedTask;}));undoRow.Children.Add(Button("REDO",()=>{EndStroke();if(project is not null&&history.Redo(project)){unsaved=true;Request(false);}return Task.CompletedTask;}));left.Children.Add(undoRow);
        left.Children.Add(Button("CLEAR MANUAL EDITS",async()=>
        {
            if(project is null||!await Confirm("Clear manual corrections?","This clears Add, Remove and Protection layers and undo history. Automatic detection stays."))return;
            Array.Clear(project.Added);Array.Clear(project.Removed);Array.Clear(project.Protected);history.Clear();unsaved=true;Request(false);
        }));
        columns.Children.Add(new ScrollViewer { Content=left,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetColumn(canvas,1);columns.Children.Add(canvas);
        right.Children.Add(Group("FOLIAGE DETECTION",pickInfo,
            Number("Hue centre",hue,true),Number("Hue tolerance",tolerance,true),Number("Minimum saturation",saturation,true),Number("Minimum brightness (lower includes dark leaves)",dark,true),
            Number("Minimum source alpha",alpha,true),Number("Canopy bias (0 = none)",canopy,true),Number("Selection edge softness",softness,true),Number("Grow / shrink",grow,true),Number("Remove islands ≤ pixels",islands,true),Number("Picked-colour tolerance",colourDistance,true),
            Button("AUTO DETECT / RESET AUTO ONLY",()=>{if(project is not null){ReadSettings();Request(true);}return Task.CompletedTask;}),
            Button("USE HUE DETECTOR",()=>{pickedColour=null;pickInfo.Text="Detector: hue selection";Changed(true);return Task.CompletedTask;})));
        right.Children.Add(Group("MASK EDITING",Label("Brush mode",brush),Number("Brush radius (source pixels)",radius,false,false),Number("Brush strength",strength,false,false),Number("Overlay opacity",overlay,false,false)));
        right.Children.Add(Group("SEASONAL PREVIEW",season,Number("Effect strength",effect,false),Number("Autumn progress: yellow → orange → red",autumn,false),
            new TextBlock { Text="Winter keeps leaves. Snow is an offline approximation; Godot runtime snow remains separate.",TextWrapping=TextWrapping.Wrap },Number("Winter snow coverage",snow,false),Number("Snow patch size",patch,false),presetName,
            Button("SAVE LOOK PRESET",async()=>await RunJob(async token=>{var look=ReadLook();string name=presetName.Text??"";await Task.Run(()=>SeasonalProjectStore.SaveLook(name,look,token),token);status.Text="Look preset saved (colour/snow settings only).";})),
            Button("LOAD LOOK PRESET",async()=>await LoadLook())));
        right.Children.Add(Group("LEAFLESS / BRANCH PROTOTYPE",
            new TextBlock { Text="Experimental procedural sketch. Original wood stays fixed; hidden branches are invented. Protect visible wood first. Orange = root; cyan = tips. Markers are not exported.",TextWrapping=TextWrapping.Wrap },branchInfo,
            Button("MARK TRUNK CONNECTION",()=>{brush.SelectedIndex=(int)MaskBrush.BranchRoot;view.SelectedIndex=(int)StudioView.Leafless;return Task.CompletedTask;}),
            Button("MARK BRANCH TIPS (up to 16)",()=>{brush.SelectedIndex=(int)MaskBrush.BranchTip;view.SelectedIndex=(int)StudioView.Leafless;return Task.CompletedTask;}),
            Button("REMOVE LAST TIP",()=>{if(project is not null){project.Branches=project.Branches with { Tips=(project.Branches.Tips??Array.Empty<BranchPoint>()).SkipLast(1).ToArray() };BranchChanged(false);}return Task.CompletedTask;}),
            Button("CLEAR CONNECTION POINTS",()=>{if(project is not null){project.Branches=project.Branches with { Root=null,Tips=Array.Empty<BranchPoint>() };BranchChanged(false);}return Task.CompletedTask;}),
            Number("Branch base thickness (source pixels)",branchThickness,false,false),Number("Curvature",branchCurve,false,false),Number("Forks per branch",branchForks,false,false),Number("Seed",branchSeed,false,false),Number("Leaf removal cutoff",branchCutoff,false,false),Label("Wood colour (#RRGGBB)",branchColour),Number("Wood colour variation",branchVariation,false,false),preserveBrown,
            Button("SAMPLE SOURCE WOOD COLOUR",()=>{if(project is not null)branchColour.Text=BranchPrototypeService.SampleWood(project);return Task.CompletedTask;}),
            Button("PREVIEW LEAF REMOVAL",()=>{view.SelectedIndex=(int)StudioView.Leafless;Request(false);return Task.CompletedTask;}),
            Button("GENERATE / UPDATE BRANCHES",()=>{view.SelectedIndex=(int)StudioView.Branches;Request(false);return Task.CompletedTask;}),
            Button("NEXT SEED",()=>{branchSeed.Value=(branchSeed.Value+1)%10000;return Task.CompletedTask;}),
            Button("ACTUAL SIZE (1×)",()=>{canvas.ActualSize();return Task.CompletedTask;}),Button("EXPORT LEAFLESS PROTOTYPE…",async()=>await ExportBranches())));
        right.Children.Add(Group("PROJECT / EXPORT",Button("SAVE MASK PROJECT AS…",async()=>await SaveProject()),Button("OPEN MASK PROJECT…",async()=>await OpenProject()),Button("EXPORT GODOT FOLIAGE MASK…",async()=>await ExportMask()),
            new TextBlock { Text="Exports grayscale RGB with opaque alpha, exactly matching source dimensions. Existing files are not overwritten. Seasonal PNG exports are a later phase.",TextWrapping=TextWrapping.Wrap }));
        var rightScroll=new ScrollViewer { Content=right,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };Grid.SetColumn(rightScroll,2);columns.Children.Add(rightScroll);
        var footer=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=8 };footer.Children.Add(status);Grid.SetColumn(cancel,1);footer.Children.Add(cancel);Grid.SetRow(footer,2);root.Children.Add(footer);Content=root;
        cancel.Click+=(_,_)=>running?.Cancel();
        view.SelectionChanged+=(_,_)=>{UpdateBranchPoints();Request(false);};season.SelectionChanged+=(_,_)=>Changed(false);
        foliageVisible.IsCheckedChanged+=(_,_)=>Request(false);protectionVisible.IsCheckedChanged+=(_,_)=>Request(false);
        brush.SelectionChanged+=(_,_)=>{EndStroke();canvas.Mode=(MaskBrush)Math.Max(0,brush.SelectedIndex);};
        radius.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)canvas.BrushRadius=radius.Value;};
        foreach(var slider in new[]{branchThickness,branchCurve,branchForks,branchSeed,branchCutoff,branchVariation})
            slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty)BranchChanged(true);};
        branchColour.TextChanged+=(_,_)=>BranchChanged(true);preserveBrown.IsCheckedChanged+=(_,_)=>BranchChanged(true);
        canvas.BranchPointPicked=PlaceBranchPoint;
        canvas.Paint=Paint;canvas.StrokeEnded=EndStroke;canvas.Pick=Pick;
        DragDrop.SetAllowDrop(canvas,true);
        canvas.AddHandler(DragDrop.DragOverEvent,(_,e)=>{e.DragEffects=e.DataTransfer.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        canvas.AddHandler(DragDrop.DropEvent,async(_,e)=>
        {
            var paths=e.DataTransfer.TryGetFiles()?.Where(f=>f.Path.IsFile&&Path.GetExtension(f.Path.LocalPath).Equals(".png",StringComparison.OrdinalIgnoreCase)).Select(f=>f.Path.LocalPath).ToArray()??Array.Empty<string>();
            e.Handled=true;if(paths.Length!=1){status.Text="Drop exactly one PNG in this editor.";return;}await LoadSprite(paths[0]);
        });
        Closing+=(_,e)=>{if(unsaved&&!closed){e.Cancel=true;_=CloseWithConfirmation();}};
        Closed+=(_,_)=>{closed=true;running?.Cancel();lifetime.Cancel();canvas.Source=null;bitmap?.Dispose();bitmap=null;if(!pumping)DisposeLifetime();};
    }
    private async Task CloseWithConfirmation()
    {
        if(confirmingClose)return;confirmingClose=true;
        try{if(await Confirm("Unsaved mask edits","Close and discard unsaved mask/project changes?")){unsaved=false;Close();}}
        finally{confirmingClose=false;}
    }
    private static Slider S(double min,double max,double value,bool integer=false)=>new(){Minimum=min,Maximum=max,Value=value,IsSnapToTickEnabled=integer,TickFrequency=integer?1:0.01};
    private static StackPanel Label(string title,Control control)=>new(){Spacing=4,Children={new TextBlock { Text=title,FontSize=11,TextWrapping=TextWrapping.Wrap },control}};
    private static Expander Group(string title,params Control[] controls)
    {var panel=new StackPanel { Spacing=8,Margin=new Thickness(0,8) };foreach(var c in controls)panel.Children.Add(c);return new Expander { Header=title,Content=panel,IsExpanded=true };}
    private Button Button(string title,Func<Task> action)
    {
        var button=new Button { Content=title,HorizontalAlignment=HorizontalAlignment.Stretch };
        button.Click+=async(_,_)=>{if(busy)return;EndStroke();try{await action();}catch(Exception ex){if(!closed)status.Text=ex.Message;}};return button;
    }
    private Control Number(string title,Slider slider,bool detector,bool processing=true)
    {
        bool syncing=false;var box=new TextBox { Width=72,Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture) };
        var row=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=8 };row.Children.Add(slider);Grid.SetColumn(box,1);row.Children.Add(box);
        slider.PropertyChanged+=(_,e)=>
        {
            if(e.Property!=Slider.ValueProperty)return;
            if(!syncing)box.Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture);
            if(processing)Changed(detector);else if(ReferenceEquals(slider,overlay))Request(false);
        };
        void Commit()
        {
            if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double number)&&double.IsFinite(number)&&number>=slider.Minimum&&number<=slider.Maximum)
            {syncing=true;slider.Value=slider.IsSnapToTickEnabled?Math.Round(number):number;syncing=false;box.Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture);}
            else {box.Text=slider.Value.ToString("0.##",CultureInfo.InvariantCulture);status.Text=$"{title}: enter {slider.Minimum}–{slider.Maximum}.";}
        }
        box.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}};box.LostFocus+=(_,_)=>Commit();
        return Label(title,row);
    }
    private SeasonalLook ReadLook()=>new((StudioSeason)Math.Max(0,season.SelectedIndex),effect.Value,autumn.Value,snow.Value,(int)patch.Value);
    private void ReadSettings()
    {
        if(project is null)return;
        project.Detection=new(hue.Value,tolerance.Value,saturation.Value,dark.Value,(int)alpha.Value,canopy.Value,softness.Value,(int)grow.Value,(int)islands.Value,pickedColour,colourDistance.Value);
        project.Look=ReadLook();
    }
    private void Changed(bool detector){if(loadingControls)return;ReadSettings();if(project is not null){unsaved=true;if(!detector)view.SelectedIndex=(int)StudioView.Seasonal;}Request(detector);}
    private void SetControls()
    {
        if(project is null)return;loadingControls=true;var d=project.Detection;var l=project.Look;
        hue.Value=d.Hue;tolerance.Value=d.Tolerance;saturation.Value=d.MinSaturation;dark.Value=d.MinLight;alpha.Value=d.MinAlpha;canopy.Value=d.CanopyBias;softness.Value=d.Softness;grow.Value=d.Grow;islands.Value=d.RemoveIslands;colourDistance.Value=d.ColourTolerance;pickedColour=d.PickColour;
        season.SelectedIndex=(int)l.Season;effect.Value=l.Strength;autumn.Value=l.AutumnProgress;snow.Value=l.Snow;patch.Value=l.SnowPatch;
        pickInfo.Text=pickedColour is null?"Detector: hue selection":"Picked colour: "+pickedColour;
        var branch=project.Branches;branchThickness.Value=branch.Thickness;branchCurve.Value=branch.Curvature;branchForks.Value=branch.Forks;branchSeed.Value=branch.Seed;branchCutoff.Value=branch.LeafCutoff;branchColour.Text=branch.Colour;branchVariation.Value=branch.ColourVariation;preserveBrown.IsChecked=branch.PreserveBrown;
        UpdateBranchPoints();loadingControls=false;
    }
    private void Request(bool detector)
    {
        if(closed||loadingControls||project is null)return;dirty=true;detectDirty|=detector;if(!busy&&!pumping)_=Pump();
    }
    private async Task Pump()
    {
        pumping=true;
        try
        {
            while(dirty&&!closed&&!busy&&project is not null)
            {
                int delay=(int)Math.Max(0,120-(Environment.TickCount64-lastPreview));if(delay>0)await Task.Delay(delay,lifetime.Token);
                if(closed||busy||project is null)break;dirty=false;lastPreview=Environment.TickCount64;
                var current=project;bool detect=detectDirty;detectDirty=false;
                try
                {
                    if(detect)
                    {
                        var detection=current.Detection;
                        var mask=await Task.Run(()=>SeasonalMaskService.Detect(current,detection,lifetime.Token),lifetime.Token);
                        if(closed||busy||!ReferenceEquals(project,current)){detectDirty=true;dirty=true;continue;}
                        if(current.Detection!=detection){detectDirty=true;dirty=true;continue;}current.Automatic=mask;detectedSettings=detection;
                        if(!mask.Any(v=>v>0))status.Text="No obvious foliage found. Adjust detection or paint Add Foliage.";
                    }
                    var snapshot=current.Snapshot();var mode=(StudioView)Math.Max(0,view.SelectedIndex);double opacity=overlay.Value;bool foliage=foliageVisible.IsChecked==true,protect=protectionVisible.IsChecked==true;
                    var data=await Task.Run(()=>mode is StudioView.Leafless or StudioView.Branches ? BranchPrototypeService.Render(snapshot,snapshot.Branches,mode==StudioView.Branches,lifetime.Token) : SeasonalMaskService.Render(snapshot,mode,opacity,foliage,protect,lifetime.Token),lifetime.Token);
                    if(closed||busy||!ReferenceEquals(project,current))continue;
                    using var stream=new MemoryStream(data);var next=new Bitmap(stream);var previous=bitmap;bitmap=next;canvas.Source=next;previous?.Dispose();
                }
                catch(OperationCanceledException){}
                catch(Exception ex){if(!closed)status.Text="Preview failed: "+ex.Message;}
            }
        }
        catch(OperationCanceledException){}
        finally{pumping=false;if(closed)DisposeLifetime();}
    }
    private void DisposeLifetime(){if(!lifetimeDisposed){lifetimeDisposed=true;lifetime.Dispose();}}
    private void Paint(Point point)
    {
        if(project is null||busy)return;
        int mode=(int)canvas.Mode;if(mode<1||mode>4)return;
        if(strokeMode!=mode){EndStroke();strokeMode=mode;}
        double r=radius.Value;point=new Point(Math.Clamp(point.X,-r,project.Width+r),Math.Clamp(point.Y,-r,project.Height+r));
        Point previous=lastPoint??point;double length=Math.Sqrt(Math.Pow(point.X-previous.X,2)+Math.Pow(point.Y-previous.Y,2));int steps=Math.Max(1,(int)Math.Ceiling(length/Math.Max(1,r*0.4)));
        for(int step=0;step<=steps;step++)
        {
            double cx=previous.X+(point.X-previous.X)*step/steps,cy=previous.Y+(point.Y-previous.Y)*step/steps;
            for(int y=Math.Max(0,(int)(cy-r));y<Math.Min(project.Height,(int)(cy+r)+1);y++)for(int x=Math.Max(0,(int)(cx-r));x<Math.Min(project.Width,(int)(cx+r)+1);x++)
            {
                if((x-cx)*(x-cx)+(y-cy)*(y-cy)>r*r)continue;int i=y*project.Width+x;if(project.Rgba[i*4+3]==0)continue;
                if(mode==1){ChangePixel(0,i,255);ChangePixel(1,i,0);}else if(mode==2){ChangePixel(1,i,255);ChangePixel(0,i,0);}else ChangePixel(2,i,mode==3?(byte)255:(byte)0);
            }
        }
        lastPoint=point;unsaved=true;Request(false);
    }
    private void ChangePixel(int layer,int i,byte target)
    {
        if(project is null)return;long key=(long)layer*project.Automatic.Length+i;var mask=MaskHistory.Layer(project,layer);
        byte after=(byte)Math.Round(mask[i]+(target-mask[i])*strength.Value);if(mask[i]==after)return;
        if(stroke.TryGetValue(key,out var delta))stroke[key]=delta with { After=after };
        else{if(stroke.Count>=65536){status.Text="Stroke limit reached. Release and begin another stroke.";return;}stroke[key]=new(layer,i,mask[i],after);}
        mask[i]=after;
    }
    private void EndStroke(){if(stroke.Count>0){history.Push(new(stroke.Values.ToArray()));stroke.Clear();}lastPoint=null;strokeMode=null;}
    private void Pick(Point point)
    {
        if(project is null||busy)return;int x=(int)point.X,y=(int)point.Y;if(x<0||y<0||x>=project.Width||y>=project.Height)return;int i=(y*project.Width+x)*4;if(project.Rgba[i+3]==0)return;
        pickedColour=$"#{project.Rgba[i]:X2}{project.Rgba[i+1]:X2}{project.Rgba[i+2]:X2}";pickInfo.Text="Picked colour: "+pickedColour;Changed(true);
    }
    private async Task RunJob(Func<CancellationToken,Task> action)
    {
        if(busy)return;EndStroke();busy=true;left.IsEnabled=right.IsEnabled=canvas.IsEnabled=false;cancel.IsEnabled=true;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);running=cancellation;
        try{while(pumping)await Task.Delay(10,cancellation.Token);await action(cancellation.Token);}
        catch(OperationCanceledException){if(!closed)status.Text="Cancelled.";}
        catch(Exception ex){if(!closed)status.Text="Operation failed: "+ex.Message;}
        finally{running=null;busy=false;if(!closed){left.IsEnabled=right.IsEnabled=canvas.IsEnabled=true;cancel.IsEnabled=false;if(dirty)Request(false);}}
    }
    private static readonly FilePickerFileType PngType=new("PNG sprite"){Patterns=new[]{"*.png"}};
    private static readonly FilePickerFileType ProjectType=new("Seasonal project"){Patterns=new[]{"*.seasonal.json"}};
    private async Task<string?> PickFile(FilePickerFileType type,string? folder=null)
    {var location=folder is not null && Directory.Exists(folder)?await StorageProvider.TryGetFolderFromPathAsync(folder):null;
        var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple=false,FileTypeFilter=new[]{type},SuggestedStartLocation=location });return files.Count>0&&files[0].Path.IsFile?files[0].Path.LocalPath:null;}
    private async Task<string?> SaveFile(string name,string extension)
    {var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName=name,DefaultExtension=extension,ShowOverwritePrompt=true });return file?.Path.IsFile==true?file.Path.LocalPath:null;}
    private async Task<bool> Discard()=>!unsaved||await Confirm("Unsaved edits","Discard the current unsaved mask edits and open another source/project?");
    private async Task LoadSprite(string? path=null)
    {
        if(busy||!await Discard())return;path??=await PickFile(PngType);if(path is null)return;
        await RunJob(async token=>
        {
            var loaded=await Task.Run(()=>SeasonalMaskService.Load(path,token),token);if(closed)return;
            project=loaded;canvas.Source=null;bitmap?.Dispose();bitmap=null;view.SelectedIndex=(int)StudioView.Overlay;detectedSettings=null;projectFile=null;history.Clear();unsaved=true;SetControls();sourceInfo.Text=$"{Path.GetFileName(path)}\n{loaded.Width}×{loaded.Height}";dirty=true;detectDirty=true;
            status.Text="Heuristic foliage selection. Correct bark/branches with Protect Trunk. Pink overlay = protection.";
        });
    }
    private async Task EnsureDetection(CancellationToken token)
    {
        if(project is null||detectedSettings==project.Detection)return;
        var current=project;var options=current.Detection;
        var automatic=await Task.Run(()=>SeasonalMaskService.Detect(current,options,token),token);
        token.ThrowIfCancellationRequested();current.Automatic=automatic;detectedSettings=options;detectDirty=false;dirty=true;
    }
    private async Task SaveProject()
    {
        if(project is null)return;var path=await SaveFile(Path.GetFileNameWithoutExtension(project.SourcePath)+".seasonal.json","json");if(path is null)return;
        await RunJob(async token=>{await EnsureDetection(token);var snapshot=project.Snapshot();await Task.Run(()=>SeasonalProjectStore.Save(snapshot,path,token),token);projectFile=path;unsaved=false;sourceInfo.Text=$"{Path.GetFileName(project.SourcePath)}\n{project.Width}×{project.Height}\n{Path.GetFileName(path)}";status.Text="Project saved: "+path;});
    }
    private async Task OpenProject()
    {
        if(!await Discard())return;var path=await PickFile(ProjectType);if(path is null)return;
        await RunJob(async token=>
        {
            SeasonalProjectLoad loaded;
            try{loaded=await Task.Run(()=>SeasonalProjectStore.Load(path,token),token);}
            catch(Exception ex) when(ex is FileNotFoundException or DirectoryNotFoundException)
            {
                if(!await Confirm("Source PNG missing","Choose the source PNG to relink this project? Dimensions must match."))return;
                var relink=await PickFile(PngType);if(relink is null)return;loaded=await Task.Run(()=>SeasonalProjectStore.Load(path,token,relink),token);
            }
            if(loaded.SourceChanged&&!await Confirm("Source image changed","This image has the same dimensions but a different hash. Explicitly reuse the saved masks? Check alignment carefully."))return;
            if(closed)return;project=loaded.Project;canvas.Source=null;bitmap?.Dispose();bitmap=null;view.SelectedIndex=(int)StudioView.Overlay;detectedSettings=project.Detection;projectFile=path;history.Clear();unsaved=loaded.SourceChanged;SetControls();dirty=true;detectDirty=false;
            sourceInfo.Text=$"{Path.GetFileName(project.SourcePath)}\n{project.Width}×{project.Height}\n{Path.GetFileName(path)}";status.Text=loaded.SourceChanged?"Relinked changed source. Inspect all mask alignment before exporting.":"Mask project restored exactly.";
        });
    }
    private void BranchChanged(bool updateOptions)
    {
        if(loadingControls||project is null)return;
        if(updateOptions)project.Branches=project.Branches with { Thickness=branchThickness.Value,Curvature=branchCurve.Value,Forks=(int)branchForks.Value,Seed=(int)branchSeed.Value,LeafCutoff=(int)branchCutoff.Value,Colour=(branchColour.Text??"").Trim(),ColourVariation=branchVariation.Value,PreserveBrown=preserveBrown.IsChecked==true };
        unsaved=true;UpdateBranchPoints();Request(false);
    }
    private void UpdateBranchPoints()
    {
        canvas.BranchRoot=project?.Branches.Root;canvas.BranchTips=project?.Branches.Tips??Array.Empty<BranchPoint>();
        canvas.ShowBranchPoints=view.SelectedIndex is (int)StudioView.Leafless or (int)StudioView.Branches;
        branchInfo.Text=$"Root: {(canvas.BranchRoot is null?"not marked":$"{canvas.BranchRoot.X:0}, {canvas.BranchRoot.Y:0}")} • {canvas.BranchTips.Length}/16 tips";
        canvas.InvalidateVisual();
    }
    private void PlaceBranchPoint(Point point,bool root)
    {
        if(project is null||busy||point.X<0||point.Y<0||point.X>=project.Width||point.Y>=project.Height)return;
        var picked=new BranchPoint(Math.Round(point.X),Math.Round(point.Y));picked=picked with { X=Math.Min(project.Width-1,picked.X),Y=Math.Min(project.Height-1,picked.Y) };
        if(root)project.Branches=project.Branches with { Root=picked };
        else
        {
            var tips=project.Branches.Tips??Array.Empty<BranchPoint>();if(tips.Length>=16){status.Text="Maximum 16 tips. Remove one or clear points.";return;}
            project.Branches=project.Branches with { Tips=tips.Append(picked).ToArray() };
        }
        BranchChanged(false);
    }
    private async Task ExportBranches()
    {
        if(project is null)return;
        var path=await SaveFile(Path.GetFileNameWithoutExtension(project.SourcePath)+"_leafless_prototype.png","png");if(path is null)return;
        await RunJob(async token=>
        {
            await EnsureDetection(token);var snapshot=project.Snapshot();
            var data=await Task.Run(()=>BranchPrototypeService.Render(snapshot,snapshot.Branches,true,token),token);
            await Task.Run(()=>SeasonalMaskService.SaveNew(path,data,token),token);
            status.Text=$"Exported branch prototype {snapshot.Width}×{snapshot.Height}. Protected/source wood stays in place; no markers exported.";
        });
    }
    private async Task ExportMask()
    {
        if(project is null)return;var path=await SaveFile(Path.GetFileNameWithoutExtension(project.SourcePath)+"_foliage_mask.png","png");if(path is null)return;
        await RunJob(async token=>{await EnsureDetection(token);var snapshot=project.Snapshot();await Task.Run(()=>SeasonalMaskService.ExportMask(snapshot,path,token),token);status.Text=$"Exported {snapshot.Width}×{snapshot.Height} grayscale RGB / opaque alpha: {path}";});
    }
    private async Task LoadLook()
    {
        if(project is null){status.Text="Load a source sprite first.";return;}
        var path=await PickFile(new FilePickerFileType("Seasonal look preset"){Patterns=new[]{"*.json"}},SeasonalProjectStore.PresetFolder);if(path is null)return;
        await RunJob(async token=>{var look=await Task.Run(()=>SeasonalProjectStore.LoadLook(path),token);if(project is not null){project.Look=look;SetControls();unsaved=true;dirty=true;}status.Text="Seasonal look loaded. Select Seasonal Preview to inspect.";});
    }
    private async Task<bool> Confirm(string title,string message)
    {
        var dialog=new Window { Title=title,Width=470,Height=230,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush.Parse("#102338"),Foreground=Brushes.White };
        var yes=new Button { Content="CONTINUE" };var no=new Button { Content="CANCEL" };
        yes.Click+=(_,_)=>dialog.Close(true);no.Click+=(_,_)=>dialog.Close(false);
        dialog.Content=new StackPanel { Margin=new Thickness(20),Spacing=18,Children={new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap },new StackPanel { Orientation=Orientation.Horizontal,Spacing=10,Children={yes,no} }} };
        return await dialog.ShowDialog<bool>(this);
    }
}
