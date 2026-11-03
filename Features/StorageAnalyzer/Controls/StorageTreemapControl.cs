using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Treemap;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Controls;

/// <summary>
/// WinDirStat-like full-file treemap, rendered as ONE cached bitmap. All
/// included positive-length files have geometry; tiny rectangles may share
/// physical pixels until zoomed. No per-file Controls or redraw timers.
/// </summary>
public sealed class StorageTreemapControl : Control
{
    private static readonly IBrush CanvasBackground = Brush.Parse("#061B28");
    private static readonly Pen SelectedPen = new(Brush.Parse("#E9FFFF"), 2);
    private static readonly Pen HoverPen = new(Brush.Parse("#66F7ED"), 1.5);
    private static readonly Pen FolderPen = new(Brush.Parse("#B5ECFA"), 2);

    private StorageNode? _directory;
    private StorageNode? _selected;
    private long _minimumBytes;
    private CancellationTokenSource? _rebuild;
    private long _generation;
    private FileTreemapSnapshot? _snapshot;
    private WriteableBitmap? _bitmap;
    private FileTreemapTile? _hovered;
    private (string Text, Rect Bounds)[] _folderLabels = Array.Empty<(string, Rect)>();
    private (string Text, Rect Bounds)[] _fileLabels = Array.Empty<(string, Rect)>();
    private string? _error;

    public event Action<StorageNode>? NodeSelected;
    public event Action<StorageNode>? DirectoryRequested;
    public event Action<TreemapEntry?>? HoverChanged;
    public event Action<FileTreemapSnapshot>? LayoutReady;
    public event Action<string>? LayoutFailed;

    public StorageTreemapControl()
    {
        Height = 390;
        MinWidth = 170;
        Focusable = true;
        ClipToBounds = true;
        ToolTip.SetShowDelay(this, 300);
        SizeChanged += (_, _) => QueueBuild();
        AttachedToVisualTree += (_, _) => QueueBuild();
        DetachedFromVisualTree += (_, _) => CancelBuild();
    }

    public void ShowDirectory(StorageNode? directory, long minimumFileBytes = 0)
    {
        if (ReferenceEquals(_directory, directory)
            && _minimumBytes == minimumFileBytes && _snapshot is { } ready)
        {
            LayoutReady?.Invoke(ready);
            return;
        }

        _directory = directory;
        _minimumBytes = Math.Max(0, minimumFileBytes);
        if (directory is null)
            _selected = null;
        _snapshot = null;
        _folderLabels = Array.Empty<(string, Rect)>();
        _fileLabels = Array.Empty<(string, Rect)>();
        _error = null;
        _hovered = null;
        ToolTip.SetTip(this, null);
        HoverChanged?.Invoke(null);
        _bitmap?.Dispose();
        _bitmap = null;
        QueueBuild();
    }

    public StorageNode? SelectedNode
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
                return;
            _selected = value;
            InvalidateVisual();
        }
    }

    /// <summary>Cancel a pending build and release old scan references.</summary>
    public void Clear() => ShowDirectory(null);

    private void CancelBuild()
    {
        _generation++;
        _rebuild?.Cancel();
    }

    private void QueueBuild()
    {
        CancelBuild();
        var directory = _directory;
        int width = (int)Math.Ceiling(Bounds.Width);
        int height = (int)Math.Ceiling(Bounds.Height);
        if (_snapshot is { } previous &&
            (previous.Width != width || previous.Height != height))
        {
            // Never hit-test a bitmap still stretched from an older viewport.
            _snapshot = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _hovered = null;
        }
        if (directory is null || width < 1 || height < 1)
        {
            InvalidateVisual();
            return;
        }

        // Cap extreme accidental viewport sizes before allocating pixel arrays.
        if (width > 8192 || height > 8192)
            return;
        var generation = _generation;
        var cts = new CancellationTokenSource();
        _rebuild = cts;
        RebuildAsync(directory, width, height, _minimumBytes, generation, cts);
    }

    private async void RebuildAsync(StorageNode directory, int width, int height,
        long minimumBytes, long generation, CancellationTokenSource cts)
    {
        try
        {
            var snapshot = await Task.Run(() =>
                FileTreemapLayout.Build(directory, width, height, minimumBytes, cts.Token),
                cts.Token);
            if (cts.IsCancellationRequested || generation != _generation)
                return;

            // Only the final bitmap upload and Avalonia drawing run on the UI
            // thread. Geometry, file colours and millions of pixel writes
            // are computed by the worker.
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            try
            {
                using (var frame = bitmap.Lock())
                {
                    int lineBytes = width * 4;
                    if (frame.RowBytes == lineBytes)
                        Marshal.Copy(snapshot.BgraPixels, 0,
                            frame.Address, snapshot.BgraPixels.Length);
                    else
                    {
                        for (int y = 0; y < height; ++y)
                            Marshal.Copy(snapshot.BgraPixels, y * lineBytes,
                                IntPtr.Add(frame.Address, y * frame.RowBytes), lineBytes);
                    }
                }
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            _bitmap?.Dispose();
            _bitmap = bitmap;
            _snapshot = snapshot;
            _error = null;
            _folderLabels = snapshot.Folders
                .Where(x => x.Depth is > 0 and <= 2
                    && x.Rect.Width >= 110 && x.Rect.Height >= 46
                    && x.Rect.Area >= 6000)
                .OrderByDescending(x => x.Rect.Area)
                .Take(16)
                .Select(x => (x.Folder.Name, new Rect(
                    x.Rect.X, x.Rect.Y, x.Rect.Width, x.Rect.Height)))
                .ToArray();
            _fileLabels = snapshot.Files
                .Where(x => x.Rect.Width >= 85 && x.Rect.Height >= 52
                    && x.Rect.Area >= 4500)
                .OrderByDescending(x => x.Rect.Area)
                .Take(18)
                .Select(x => (x.File.Name + "  " + FormatBytes(x.File.LogicalBytes),
                    new Rect(x.Rect.X, x.Rect.Y, x.Rect.Width, x.Rect.Height)))
                .ToArray();
            InvalidateVisual();
            LayoutReady?.Invoke(snapshot);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (generation == _generation)
            {
                _error = e.Message;
                LayoutFailed?.Invoke(e.Message);
                InvalidateVisual();
            }
        }
        finally
        {
            if (ReferenceEquals(_rebuild, cts))
                _rebuild = null;
            cts.Dispose();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(CanvasBackground, rect);
        if (_bitmap is { } bitmap)
        {
            context.DrawImage(bitmap, new Rect(bitmap.Size), rect);
            if (_snapshot is { VisibleFiles: 0 })
                DrawHint(context, "No positive-size files meet this map filter.");

            foreach (var (text, bounds) in _folderLabels)
                DrawLabel(context, text, bounds, 12, Brushes.White);
            foreach (var (text, bounds) in _fileLabels)
                DrawLabel(context, text, bounds, 10, Brushes.White);

            if (_selected is { } selected && _snapshot is { } snapshot)
            {
                if (!selected.IsDirectory
                    && snapshot.FileIndex.TryGetValue(selected, out int index))
                    DrawOutline(context, snapshot.Files[index].Rect, SelectedPen);
                else if (selected.IsDirectory)
                {
                    // Folder selection uses the original nested rectangle.
                    foreach (var folder in snapshot.Folders)
                    {
                        if (!ReferenceEquals(folder.Folder, selected)) continue;
                        DrawOutline(context, folder.Rect, FolderPen);
                        break;
                    }
                }
            }

            if (_hovered is { } hover && hover.Rect.Area >= 4)
                DrawOutline(context, hover.Rect, HoverPen);
        }
        else
            DrawHint(context, _error ??
                (_directory is null ? "Scan a drive to see the file-level map."
                    : "Building file-level treemap…"));
    }

    private static void DrawLabel(DrawingContext context, string text,
        Rect box, double fontSize, IBrush brush)
    {
        string fitted = text.Length > 65 ? text[..64] + "…" : text;
        using (context.PushClip(box))
        {
            var formatted = new FormattedText(fitted,
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Inter"), fontSize, brush);
            context.DrawText(formatted, new Point(box.X + 5, box.Y + 5));
        }
    }

    private static void DrawOutline(DrawingContext context,
        TreemapRect box, Pen pen)
    {
        if (box.Width < 2 || box.Height < 2)
            return;
        var rect = new Rect(box.X + 1, box.Y + 1,
            Math.Max(0, box.Width - 2), Math.Max(0, box.Height - 2));
        context.DrawRectangle(null, pen, rect);
    }

    private static void DrawHint(DrawingContext context, string message)
    {
        var formatted = new FormattedText(message,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter"), 13, Brushes.LightGray);
        context.DrawText(formatted, new Point(12, 18));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var snapshot = _snapshot;
        if (snapshot is null) return;
        var point = e.GetPosition(this);
        var tile = snapshot.HitTest(point.X, point.Y);
        if (Nullable.Equals(_hovered, tile))
            return;

        _hovered = tile;
        if (tile is { } hit)
        {
            var file = hit.File;
            ToolTip.SetTip(this, $"{file.Name}\n{FormatBytes(file.LogicalBytes)}\n{file.GetFullPath()}");
            HoverChanged?.Invoke(new TreemapEntry(file, file.Name, file.LogicalBytes));
        }
        else
        {
            ToolTip.SetTip(this, null);
            HoverChanged?.Invoke(null);
        }
        InvalidateVisual(); // Cached bitmap remains unchanged on hover.
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = null;
        ToolTip.SetTip(this, null);
        HoverChanged?.Invoke(null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || _snapshot is null)
            return;

        var point = e.GetPosition(this);
        var file = _snapshot.HitTest(point.X, point.Y)?.File;
        if (file is null) return;

        NodeSelected?.Invoke(file);
        if (e.ClickCount >= 2 && file.Parent is { } parent
            && !ReferenceEquals(parent, _directory))
            DirectoryRequested?.Invoke(parent);
        e.Handled = true;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:N1} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):N1} MiB";
        return $"{bytes / (1024d * 1024 * 1024):N2} GiB";
    }
}
