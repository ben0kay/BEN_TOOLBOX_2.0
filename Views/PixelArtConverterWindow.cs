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
    private readonly ObservableCollection<string> files = new();
    private readonly ListBox queue = new();
    private readonly Slider detail = new() { Minimum = 16, Maximum = 512, Value = 128, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock settings = new(), status = new() { Text = "Add PNGs. Originals remain unchanged.", TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox nearest = new() { Content = "Nearest source sampling (already pixelated art)" };
    private readonly PixelViewport preview = new();
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

    public PixelArtConverterWindow()
    {
        Title = "Pixel Art Converter / Preview"; Width = 1180; Height = 820; MinWidth = 800; MinHeight = 550;
        Background = Brush.Parse("#091522"); Foreground = Brush.Parse("#E8F4FF");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        RenderOptions.SetBitmapInterpolationMode(preview, BitmapInterpolationMode.None);
        var body = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 12 };
        body.Children.Add(new TextBlock { Text = "PIXEL ART CONVERTER", FontSize = 24, Foreground = Brush.Parse("#86E5F6") });
        var controls = new StackPanel { Spacing = 8 };
        controls.Children.Add(settings); controls.Children.Add(detail);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (int value in new[] { 32, 64, 96, 128, 256 })
        {
            var button = new Button { Content = value.ToString() };
            button.Click += (_, _) => detail.Value = value; presets.Children.Add(button);
        }
        controls.Children.Add(presets); controls.Children.Add(nearest);
        var colourControls = new WrapPanel { ItemSpacing = 14, LineSpacing = 8 };
        colourControls.Children.Add(Field("PALETTE", palette)); colourControls.Children.Add(Field("COLOUR MATCHING", matching));
        colourControls.Children.Add(Adjustment("BRIGHTNESS", brightness)); colourControls.Children.Add(Adjustment("CONTRAST", contrast));
        colourControls.Children.Add(Adjustment("SATURATION", saturation)); colourControls.Children.Add(Adjustment("BAYER 4×4 STRENGTH", dither));
        var fit = new Button { Content = "FIT PREVIEW" };
        fit.Click += (_, _) => preview.Fit(); colourControls.Children.Add(fit);
        var reset = new Button { Content = "RESET COLOURS" }; colourControls.Children.Add(reset);
        reset.Click += (_, _) => { palette.SelectedIndex = 0; matching.SelectedIndex = 0; brightness.Value = contrast.Value = saturation.Value = 1; dither.Value = 0; };
        controls.Children.Add(colourControls); controls.Children.Add(previewInfo);
        controls.Children.Add(new TextBlock { Text = "One shared setting for all files • Original canvas dimensions retained • Preview only; export/handoff follow in P3/P4", TextWrapping = TextWrapping.Wrap });
        var settingsScroll = new ScrollViewer { Content = controls, MaxHeight = 310 }; Grid.SetRow(settingsScroll, 1); body.Children.Add(settingsScroll);
        var centre = new Grid { ColumnDefinitions = new ColumnDefinitions("270,*"), ColumnSpacing = 12 };
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        var buttons = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var add = new Button { Content = "ADD PNGs" }; var remove = new Button { Content = "REMOVE" }; var clear = new Button { Content = "CLEAR" };
        buttons.Children.Add(add); buttons.Children.Add(remove); buttons.Children.Add(clear); sidebar.Children.Add(buttons);
        queue.ItemsSource = files; Grid.SetRow(queue, 1); sidebar.Children.Add(queue); centre.Children.Add(sidebar);
        var canvas = new Grid(); canvas.Children.Add(new Checkerboard()); canvas.Children.Add(preview);
        var frame = new Border { Child = canvas, ClipToBounds = true, Padding = new Thickness(8) };
        Grid.SetColumn(frame, 1); centre.Children.Add(frame);
        Grid.SetRow(centre, 2); body.Children.Add(centre); Grid.SetRow(status, 3); body.Children.Add(status); Content = body;
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
        Closed += (_, _) => { closed = true; generation++; pending?.Cancel(); preview.Source = null; bitmap?.Dispose(); bitmap = null; };
        RequestPreview();
    }

    private void AddFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
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
        if (closed) return;
        int request = ++generation;
        pending?.Cancel();
        var cancellation = new CancellationTokenSource(); pending = cancellation;
        var token = cancellation.Token;
        string? path = queue.SelectedItem as string;
        var options = new PixelArtOptions((int)Math.Round(detail.Value), nearest.IsChecked == true,
            (PixelPalette)Math.Max(0, palette.SelectedIndex), matching.SelectedIndex == 0,
            brightness.Value, contrast.Value, saturation.Value, dither.Value);
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
