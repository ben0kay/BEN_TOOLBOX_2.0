using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
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

public sealed class PixelArtConverterWindow : Window
{
    private ObservableCollection<string> files = new();
    private readonly ObservableCollection<string> manualFiles = new(), folderFiles = new();
    private readonly ComboBox inputMode = new() { ItemsSource = new[] { "Manual files", "Folder processing" }, SelectedIndex = 0 };
    private readonly CheckBox recursiveFolder = new() { Content = "Include subfolders", IsChecked = true };
    private readonly CheckBox overwriteFolder = new() { Content = "Overwrite source PNGs (backups retained)", IsVisible = false };
    private readonly TextBlock folderCaption = new() { Text = "Choose a folder to scan.", TextWrapping = TextWrapping.Wrap };
    private StackPanel? folderControls;
    private string? scannedRoot;
    private CancellationTokenSource? scanJob;
    private bool FolderMode => inputMode.SelectedIndex == 1;
    private bool InPlace => FolderMode && overwriteFolder.IsChecked == true;
    private readonly ListBox queue = new();
    private readonly Slider detail = new() { Minimum = 16, Maximum = 512, Value = 128, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock settings = new(), status = new() { Text = "Add PNGs. Originals remain unchanged.", TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox nearest = new() { Content = "Nearest source sampling (already pixelated art)" };
    private readonly PixelViewport preview = new();
    private readonly ObservableCollection<string> paletteNames = new() { "Original colours", "Automatic 16", "Automatic 32", "Automatic 64", "Endesga 32", "Grayscale 16", "Custom draft" };
    private System.Collections.Generic.List<PixelPalettePreset> savedPalettes = new();
    private string[] draftColours = PixelArtService.EndesgaColours();
    private readonly ComboBox palette = new() { ItemsSource = new[] { "Original colours", "Automatic 16", "Automatic 32", "Automatic 64", "Endesga 32", "Grayscale 16" }, SelectedIndex = 0, MinWidth = 170 };
    private readonly ComboBox matching = new() { ItemsSource = new[] { "Perceptual (Oklab)", "RGB" }, SelectedIndex = 0, MinWidth = 160 };
    private readonly Slider brightness = ColourSlider(), contrast = ColourSlider(), saturation = ColourSlider();
    private readonly Slider dither = new() { Minimum = 0, Maximum = 1, Value = 0, Width = 150 };
    private readonly TextBlock previewInfo = new();
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private CancellationTokenSource? pending;
    private Bitmap? bitmap;
    private int generation;
    private bool closed;
    private CancellationTokenSource? outputJob;
    private Control? processingControls;
    private readonly TextBox outputFolder = new() { Watermark = "Choose existing output folder" };
    private readonly CheckBox sequential = new() { Content = "Sequential names" }, selectedOnly = new() { Content = "Export selected only" };
    private readonly TextBox outputPrefix = new() { Text = "Sprite", Width = 110 }, outputStart = new() { Text = "1", Width = 65 }, outputDigits = new() { Text = "3", Width = 45 };
    private readonly Button planOutput = new() { Content = "PREVIEW OUTPUT" }, exportOutput = new() { Content = "EXPORT PNGs" }, sendBatch = new() { Content = "SEND ALL TO SPRITE BATCH" }, cancelOutput = new() { Content = "CANCEL", IsEnabled = false };
    private readonly ListBox outputPlan = new() { MaxHeight = 110 };
    private string? plannedOutputKey;

    public PixelArtConverterWindow()
    {
        files=manualFiles;
        Title = "Pixel Art Converter"; Width = 1180; Height = 820; MinWidth = 800; MinHeight = 550;
        Background = Brush.Parse("#091522"); Foreground = Brush.Parse("#E8F4FF");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RenderOptions.SetBitmapInterpolationMode(preview, BitmapInterpolationMode.None);
        var body = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), RowSpacing = 12 };
        body.Children.Add(new TextBlock { Text = "PIXEL ART CONVERTER", FontSize = 24, Foreground = Brush.Parse("#86E5F6") });
        var controls = new StackPanel { Spacing = 8 };
        controls.Children.Add(inputMode);
        folderControls = new StackPanel { IsVisible=false, Spacing=5 };
        var pickFolder=new Button { Content="CHOOSE FOLDER / SCAN PNGs" };
        pickFolder.Click+=async(_,_)=>await ScanFolder();
        folderControls.Children.Add(pickFolder);folderControls.Children.Add(recursiveFolder);folderControls.Children.Add(folderCaption);
        controls.Children.Add(folderControls);
        controls.Children.Add(settings); controls.Children.Add(detail);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (int value in new[] { 32, 64, 96, 128, 256 })
        {
            var button = new Button { Content = value.ToString() };
            button.Click += (_, _) => detail.Value = value; presets.Children.Add(button);
        }
        controls.Children.Add(presets); controls.Children.Add(nearest);
        palette.ItemsSource = paletteNames;
        ReloadPalettes();
        var colourControls = new WrapPanel { ItemSpacing = 14, LineSpacing = 8 };
        var editPalette = new Button { Content = "EDIT / CREATE PALETTE" };
        editPalette.Click += async (_, _) =>
        {
            var initial = palette.SelectedIndex >= 7 && palette.SelectedIndex - 7 < savedPalettes.Count
                ? savedPalettes[palette.SelectedIndex-7].Colours.ToArray()
                : palette.SelectedIndex == 6 ? draftColours.ToArray() : PixelArtService.EndesgaColours();
            var editor = new PixelPaletteEditorWindow(initial,
                colours => { draftColours = colours; if (palette.SelectedIndex == 6) RequestPreview(); else palette.SelectedIndex = 6; },
                (name, colours) => { draftColours = colours; ReloadPalettes(); palette.SelectedIndex = 7 + savedPalettes.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); });
            await editor.ShowDialog(this);
        };
        colourControls.Children.Add(editPalette);
        colourControls.Children.Add(Field("PALETTE", palette)); colourControls.Children.Add(Field("COLOUR MATCHING", matching));
        colourControls.Children.Add(Adjustment("BRIGHTNESS", brightness)); colourControls.Children.Add(Adjustment("CONTRAST", contrast));
        colourControls.Children.Add(Adjustment("SATURATION", saturation)); colourControls.Children.Add(Adjustment("BAYER 4×4 STRENGTH", dither));
        var fit = new Button { Content = "FIT PREVIEW" };
        fit.Click += (_, _) => preview.Fit(); colourControls.Children.Add(fit);
        var reset = new Button { Content = "RESET COLOURS" }; colourControls.Children.Add(reset);
        reset.Click += (_, _) => { palette.SelectedIndex = 0; matching.SelectedIndex = 0; brightness.Value = contrast.Value = saturation.Value = 1; dither.Value = 0; };
        controls.Children.Add(colourControls); controls.Children.Add(previewInfo);
        controls.Children.Add(new TextBlock { Text = "One shared setting for all files • Original canvas dimensions retained • Export PNGs here or send converted copies to Sprite Batch", TextWrapping = TextWrapping.Wrap });
        var settingsScroll = new ScrollViewer { Content = controls, MaxHeight = 310 }; processingControls=settingsScroll; Grid.SetRow(settingsScroll, 1); body.Children.Add(settingsScroll);
        var centre = new Grid { ColumnDefinitions = new ColumnDefinitions("270,*"), ColumnSpacing = 12 };
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        var buttons = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var add = new Button { Content = "ADD PNGs" }; var remove = new Button { Content = "REMOVE" }; var clear = new Button { Content = "CLEAR" };
        buttons.Children.Add(add); buttons.Children.Add(remove); buttons.Children.Add(clear); sidebar.Children.Add(buttons);
        queue.ItemsSource = files;
        queue.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((path,_)=>new TextBlock { Text=FolderMode && scannedRoot is not null ? Path.GetRelativePath(scannedRoot,path??"") : path, TextWrapping=TextWrapping.Wrap }); Grid.SetRow(queue, 1); sidebar.Children.Add(queue); centre.Children.Add(sidebar);
        var canvas = new Grid(); canvas.Children.Add(new Checkerboard()); canvas.Children.Add(preview);
        var frame = new Border { Child = canvas, ClipToBounds = true, Padding = new Thickness(8) };
        Grid.SetColumn(frame, 1); centre.Children.Add(frame);
        Grid.SetRow(centre, 2); body.Children.Add(centre); var outputPanel = BuildOutputPanel(); Grid.SetRow(outputPanel, 3); body.Children.Add(outputPanel);
        Grid.SetRow(status, 4); body.Children.Add(status); Content = body;
        DragDrop.SetAllowDrop(body, true);
        body.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        body.AddHandler(DragDrop.DropEvent, (_, e) => { AddFiles(e.DataTransfer.TryGetFiles()?.Where(f => f.Path.IsFile).Select(f => f.Path.LocalPath) ?? Array.Empty<string>()); e.Handled = true; });
        add.Click += async (_, _) =>
        {
            try
            {
                var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, Title = "Add PNG images", FileTypeFilter = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } } });
                if (!closed) AddFiles(chosen.Where(f => f.Path.IsFile).Select(f => f.Path.LocalPath));
            }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        };
        inputMode.SelectionChanged+=(_,_)=>
        {
            if(outputJob is not null || scanJob is not null)return;
            files=FolderMode ? folderFiles : manualFiles;queue.ItemsSource=files;queue.SelectedIndex=files.Count>0?0:-1;
            folderControls.IsVisible=FolderMode;add.IsVisible=!FolderMode;overwriteFolder.IsVisible=FolderMode;
            plannedOutputKey=null;outputPlan.ItemsSource=null;UpdateOutputMode();RequestPreview();
        };
        overwriteFolder.IsCheckedChanged+=(_,_)=>{plannedOutputKey=null;outputPlan.ItemsSource=null;UpdateOutputMode();};
        remove.Click += (_, _) => { int i = queue.SelectedIndex; if (i < 0) return; files.RemoveAt(i); queue.SelectedIndex = files.Count == 0 ? -1 : Math.Min(i, files.Count - 1); RequestPreview(); };
        clear.Click += (_, _) => { files.Clear(); RequestPreview(); };
        queue.SelectionChanged += (_, _) => RequestPreview();
        detail.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) RequestPreview(); };
        palette.SelectionChanged += (_, _) => { dither.IsEnabled = palette.SelectedIndex != 0; matching.IsEnabled = palette.SelectedIndex != 0; RequestPreview(); };
        matching.SelectionChanged += (_, _) => RequestPreview();
        preview.ViewChanged = UpdateZoom;
        foreach (var slider in new[] { brightness, contrast, saturation, dither })
            slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) RequestPreview(); };
        dither.IsEnabled = matching.IsEnabled = false;
        nearest.IsCheckedChanged += (_, _) => RequestPreview();
        Closed += (_, _) => { closed = true; generation++; pending?.Cancel(); outputJob?.Cancel(); scanJob?.Cancel(); preview.Source = null; bitmap?.Dispose(); bitmap = null; };
        RequestPreview();
    }

    private PixelArtOptions ReadProcessing() => new((int)Math.Round(detail.Value), nearest.IsChecked == true,
        palette.SelectedIndex >= 6 ? PixelPalette.Custom : (PixelPalette)Math.Max(0, palette.SelectedIndex), matching.SelectedIndex == 0,
        brightness.Value, contrast.Value, saturation.Value, dither.Value,
        palette.SelectedIndex >= 7 && palette.SelectedIndex - 7 < savedPalettes.Count ? savedPalettes[palette.SelectedIndex-7].Colours.ToArray() : draftColours.ToArray());

    private Control BuildOutputPanel()
    {
        var body = new StackPanel { Spacing = 7 };
        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var browse = new Button { Content = "OUTPUT FOLDER" }; folderRow.Children.Add(outputFolder); Grid.SetColumn(browse,1);folderRow.Children.Add(browse);
        browse.Click += async (_,_) =>
        {
            try { var result=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple=false }); if(!closed && result.Count>0 && result[0].Path.IsFile)outputFolder.Text=result[0].Path.LocalPath; }
            catch(Exception ex){if(!closed)status.Text=ex.Message;}
        };
        body.Children.Add(overwriteFolder);body.Children.Add(folderRow);
        var names=new WrapPanel { ItemSpacing=10,LineSpacing=5 };
        names.Children.Add(selectedOnly); names.Children.Add(sequential);names.Children.Add(Field("PREFIX",outputPrefix));names.Children.Add(Field("START",outputStart));names.Children.Add(Field("DIGITS",outputDigits));body.Children.Add(names);
        body.Children.Add(new TextBlock { Text="Default names: original_px128.png • Canvas dimensions retained • Existing files are never overwritten",FontSize=11,TextWrapping=TextWrapping.Wrap });
        var actions=new WrapPanel { ItemSpacing=8,LineSpacing=5 };
        foreach(var button in new[]{planOutput,exportOutput,sendBatch,cancelOutput})actions.Children.Add(button);
        body.Children.Add(actions);body.Children.Add(outputPlan);
        planOutput.Click+=async(_,_)=>await RunOutput(false,false);
        exportOutput.Click+=async(_,_)=>await RunOutput(true,false);
        sendBatch.Click+=async(_,_)=>await RunOutput(false,true);
        cancelOutput.Click+=(_,_)=>{outputJob?.Cancel();scanJob?.Cancel();};
        return new Expander { Header="EXPORT / SEND TO SPRITE BATCH",Content=body,IsExpanded=true };
    }
    private PixelArtExportOptions ReadExport()
    {
        string[] inputs=selectedOnly.IsChecked==true ? queue.SelectedItem is string path ? new[]{path} : Array.Empty<string>() : files.ToArray();
        int start=1,digits=3;
        if(!InPlace && (!int.TryParse(outputStart.Text,out start)||!int.TryParse(outputDigits.Text,out digits)))throw new ArgumentException("Enter valid sequence start and digits.");
        return new(inputs,(outputFolder.Text??"").Trim().Trim('"'),ReadProcessing(),!InPlace && sequential.IsChecked==true,InPlace ? "Sprite" : (outputPrefix.Text??"").Trim(),start,digits,FolderMode?scannedRoot:null,InPlace);
    }
    private async Task RunOutput(bool export,bool handoff)
    {
        if(outputJob is not null || scanJob is not null)return;
        PixelArtExportOptions? options=null;
        string[] inputs; PixelArtOptions processing;
        try
        {
            inputs=files.ToArray();processing=ReadProcessing();
            if(!handoff)
            {
                options=ReadExport();
                if(export && plannedOutputKey!=System.Text.Json.JsonSerializer.Serialize(options)){status.Text="Click PREVIEW OUTPUT for the current files, settings and naming before exporting.";return;}
            }
        }
        catch(Exception ex){status.Text=ex.Message;return;}
        using var cancellation=new CancellationTokenSource();outputJob=cancellation;
        overwriteFolder.IsEnabled=false;
        generation++;pending?.Cancel(); if(processingControls is not null)processingControls.IsEnabled=false;queue.IsEnabled=false;
        foreach(var button in new[]{planOutput,exportOutput,sendBatch})button.IsEnabled=false;cancelOutput.IsEnabled=true;
        PixelArtHandoffLease? lease=null;
        try
        {
            var progress=new Progress<string>(message=>{if(!closed)status.Text=message;});
            if(handoff)
            {
                status.Text="Preparing converted images for Sprite Batch…";
                lease=await PixelArtHandoffLease.CreateAsync(inputs,processing,progress,cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                var batch=new SpriteBatchWindow();
                try { batch.LoadPixelArt(lease);batch.Show();lease=null; }
                catch { batch.Close();throw; }
                status.Text="Converted images loaded into Sprite Batch. Choose final destination and naming there. This is a snapshot of your settings.";
            }
            else if(export)
            {
                int count=options!.Overwrite ? await PixelArtFolderService.OverwriteAsync(options,progress,cancellation.Token) : await PixelArtExportService.ExportAsync(options,progress,cancellation.Token);
                plannedOutputKey=null;
                status.Text=options.Overwrite ? $"Replaced {count} PNGs in place. Original backups: .pixel-backups inside each source folder. Rescan/select an image to preview updated files."
                    : $"Exported {count} PNGs. Original dimensions, transparency and source files preserved.";
                if(options.Overwrite){preview.Source=null;bitmap?.Dispose();bitmap=null;}
            }
            else
            {
                var plan=await PixelArtExportService.PlanAsync(options!,cancellation.Token);
                if(!closed){outputPlan.ItemsSource=plan.Select(p=>options!.Overwrite ? "OVERWRITE: "+p.Source+ $" | {p.Width}×{p.Height}" : p.Display).ToArray();plannedOutputKey=System.Text.Json.JsonSerializer.Serialize(options);exportOutput.Content=options!.Overwrite ? $"OVERWRITE {plan.Length} PNGs" : "EXPORT PNGs";
                    status.Text=options.Overwrite ? $"{plan.Length} original PNGs will be replaced. Backups retained in .pixel-backups. Review full paths, then OVERWRITE." : $"{plan.Length} outputs planned. Review names, then EXPORT PNGs.";}
            }
        }
        catch(OperationCanceledException){status.Text="Cancelled. Any completed replacements were rolled back; backups are retained. Copy exports rolled back.";plannedOutputKey=null;}
        catch(Exception ex){status.Text="Output failed: "+ex.Message;plannedOutputKey=null;}
        finally
        {
            if(lease is not null)try{lease.Dispose();}catch(Exception ex){status.Text="Temporary cleanup failed: "+ex.Message;System.Diagnostics.Trace.WriteLine(status.Text);}
            outputJob=null;overwriteFolder.IsEnabled=true;if(processingControls is not null)processingControls.IsEnabled=true;queue.IsEnabled=true;foreach(var button in new[]{planOutput,exportOutput,sendBatch})button.IsEnabled=true;cancelOutput.IsEnabled=false;
        }
    }

    private void UpdateOutputMode()
    {
        outputFolder.IsEnabled=outputPrefix.IsEnabled=outputStart.IsEnabled=outputDigits.IsEnabled=sequential.IsEnabled=!InPlace;
        exportOutput.Content=InPlace ? "OVERWRITE PNGs (preview first)" : "EXPORT PNGs";
    }
    private async Task ScanFolder()
    {
        if(outputJob is not null || scanJob is not null)return;
        try
        {
            var chosen=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple=false,Title="Choose PNG folder" });
            if(closed || chosen.Count==0 || !chosen[0].Path.IsFile)return;
            string root=chosen[0].Path.LocalPath;bool recursive=recursiveFolder.IsChecked==true;
            using var cancellation=new CancellationTokenSource();scanJob=cancellation;inputMode.IsEnabled=false;cancelOutput.IsEnabled=true;
            try
            {
                status.Text="Scanning PNG paths…";
                var result=await PixelArtFolderService.ScanAsync(root,recursive,cancellation.Token);
                if(closed)return;
                scannedRoot=root;folderFiles.Clear();foreach(var file in result.Files)folderFiles.Add(file);
                files=folderFiles;queue.ItemsSource=files;queue.SelectedIndex=files.Count>0?0:-1;
                folderCaption.Text=$"{root} • {files.Count} PNGs • {result.Skipped} inaccessible/linked entries skipped";
                plannedOutputKey=null;outputPlan.ItemsSource=null;
                RequestPreview();status.Text=folderCaption.Text;
            }
            finally{scanJob=null;inputMode.IsEnabled=true;cancelOutput.IsEnabled=false;}
        }
        catch(OperationCanceledException){status.Text="Folder scan cancelled; previous list retained.";}
        catch(Exception ex){status.Text="Scan failed: "+ex.Message;}
    }

    private void ReloadPalettes()
    {
        savedPalettes = PixelPalettePresets.Load(out var warning);
        while (paletteNames.Count > 7) paletteNames.RemoveAt(paletteNames.Count - 1);
        foreach (var preset in savedPalettes) paletteNames.Add("Saved: " + preset.Name);
        if (warning.Length > 0) status.Text = warning;
    }

    private void AddFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
        if(FolderMode){status.Text="Drag/drop belongs to Manual files. Switch modes or use Choose Folder.";return;}
        if(outputJob is not null || scanJob is not null){status.Text="Wait for the active job before adding images.";return;}
        int rejected = 0;
        foreach (string path in paths)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (!File.Exists(full) || !Path.GetExtension(full).Equals(".png", StringComparison.OrdinalIgnoreCase)) { rejected++; continue; }
                if (!files.Any(f => f.Equals(full, StringComparison.OrdinalIgnoreCase))) files.Add(full);
            }
            catch { rejected++; }
        }
        if (queue.SelectedIndex < 0 && files.Count > 0) queue.SelectedIndex = 0;
        status.Text = $"{files.Count} PNGs queued. {rejected} invalid paths skipped. Settings apply to every file.";
    }

    private async void RequestPreview()
    {
        if (closed || outputJob is not null) return;
        int request = ++generation;
        pending?.Cancel();
        var cancellation = new CancellationTokenSource(); pending = cancellation;
        var token = cancellation.Token;
        string? path = queue.SelectedItem as string;
        var options = ReadProcessing();
        settings.Text = $"LOGICAL PIXEL WIDTH: {options.LogicalWidth} • smaller = chunkier • {files.Count} files";
        preview.Source = null; bitmap?.Dispose(); bitmap = null;
        if (path is null) { status.Text = "Drop PNGs here or choose ADD PNGs."; pending = null; cancellation.Dispose(); return; }
        status.Text = "Updating selected preview…";
        try
        {
            await Task.Delay(125, token);
            await previewGate.WaitAsync(token);
            byte[] bytes;
            try { bytes = await Task.Run(() => PixelArtService.Preview(path, options, token, logicalPreview: true), token); }
            finally { previewGate.Release(); }
            if (closed || request != generation || token.IsCancellationRequested) return;
            using var stream = new MemoryStream(bytes);
            bitmap = new Bitmap(stream); preview.Source = bitmap; UpdateZoom();
            status.Text = $"{Path.GetFileName(path)} • Logical grid {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} • Original canvas retained by conversion pipeline. Transparency preserved.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed && request == generation) status.Text = "Preview failed: " + ex.Message; }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); }
    }

    private static Slider ColourSlider() => new() { Minimum = 0, Maximum = 2, Value = 1, Width = 150 };
    private static StackPanel Field(string title, Control control) => new() { Spacing = 3, Children = { new TextBlock { Text = title, FontSize = 11 }, control } };
    private static StackPanel Adjustment(string title, Slider slider)
    {
        var label = new TextBlock();
        void Update() => label.Text = $"{title}: {slider.Value:0.00}";
        slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) Update(); };
        Update(); return new StackPanel { Spacing = 3, Children = { label, slider } };
    }
    private void UpdateZoom()
    {
        if (bitmap is null) return;
        previewInfo.Text = $"Logical preview: {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} • Wheel: zoom • Left/middle drag: pan • Double-click: fit";
    }

    private sealed class PixelViewport : Control
    {
        private Bitmap? source;
        private double scale = 1;
        private Vector offset;
        private Point? drag;
        private bool fit = true;
        public Action? ViewChanged;
        public Bitmap? Source
        {
            get => source;
            set { source = value; if (fit) Fit(); InvalidateVisual(); }
        }
        public PixelViewport()
        {
            ClipToBounds = true;
            SizeChanged += (_, _) => { if (fit) Fit(); };
        }
        public void Fit()
        {
            fit = true;
            if (source is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            scale = Math.Max(0.01, Math.Min(Bounds.Width / source.Size.Width, Bounds.Height / source.Size.Height));
            offset = new Vector((Bounds.Width-source.Size.Width*scale)/2, (Bounds.Height-source.Size.Height*scale)/2);
            InvalidateVisual(); ViewChanged?.Invoke();
        }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if (source is not null)
                context.DrawImage(source, new Rect(source.Size), new Rect(offset.X,offset.Y,source.Size.Width*scale,source.Size.Height*scale));
        }
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (source is null) return;
            var point=e.GetPosition(this);
            double next=Math.Clamp(scale*Math.Pow(1.2,e.Delta.Y),0.05,64);
            double ratio=next/scale;
            offset=new Vector(point.X-(point.X-offset.X)*ratio,point.Y-(point.Y-offset.Y)*ratio);
            scale=next;fit=false;InvalidateVisual();ViewChanged?.Invoke();e.Handled=true;
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var properties=e.GetCurrentPoint(this).Properties;
            if (!properties.IsLeftButtonPressed && !properties.IsMiddleButtonPressed) return;
            if(e.ClickCount==2){Fit();e.Handled=true;return;}
            drag=e.GetPosition(this);e.Pointer.Capture(this);e.Handled=true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if(drag is not Point previous)return;
            var point=e.GetPosition(this);offset+=point-previous;drag=point;fit=false;InvalidateVisual();e.Handled=true;
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);drag=null;e.Pointer.Capture(null);
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);drag=null;
        }
    }

    private sealed class Checkerboard : Control
    {
        private static readonly IBrush Light = Brush.Parse("#334455"), Dark = Brush.Parse("#22303E");
        public override void Render(DrawingContext context)
        {
            const int size = 16;
            for (int y = 0; y < Bounds.Height; y += size)
                for (int x = 0; x < Bounds.Width; x += size)
                    context.FillRectangle(((x / size + y / size) & 1) == 0 ? Light : Dark, new Rect(x, y, size, size));
        }
    }
}
