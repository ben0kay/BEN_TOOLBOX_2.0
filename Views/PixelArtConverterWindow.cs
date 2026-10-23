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
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
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
        controls.Children.Add(new TextBlock { Text = "One shared setting for all files • Original canvas dimensions retained • Preview only; export/handoff follow in P3/P4", TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(controls, 1); body.Children.Add(controls);
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
        var options = new PixelArtOptions((int)Math.Round(detail.Value), nearest.IsChecked == true);
        settings.Text = $"LOGICAL PIXEL WIDTH: {options.LogicalWidth} • smaller = chunkier • {files.Count} files";
        preview.Source = null; bitmap?.Dispose(); bitmap = null;
        if (path is null) { status.Text = "Drop PNGs here or choose ADD PNGs."; pending = null; cancellation.Dispose(); return; }
        status.Text = "Updating selected preview…";
        try
        {
            await Task.Delay(125, token);
            await previewGate.WaitAsync(token);
            byte[] bytes;
            try { bytes = await Task.Run(() => PixelArtService.Preview(path, options, token), token); }
            finally { previewGate.Release(); }
            if (closed || request != generation || token.IsCancellationRequested) return;
            using var stream = new MemoryStream(bytes);
            bitmap = new Bitmap(stream); preview.Source = bitmap;
            status.Text = $"{Path.GetFileName(path)} • Canvas {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} retained • logical width {options.LogicalWidth} (capped at source width). Transparency preserved.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed && request == generation) status.Text = "Preview failed: " + ex.Message; }
        finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); }
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
