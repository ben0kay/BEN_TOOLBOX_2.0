using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Treemap;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Controls;

/// <summary>
/// One retained visual, at most 96 rectangles, no per-file Controls and no
/// animation timer. Geometry is rebuilt only for folder/viewport changes.
/// Click selects; double-click a folder to zoom into it.
/// </summary>
public sealed class StorageTreemapControl : Control
{
    private static readonly IBrush BackgroundBrush = Brush.Parse("#061B28");
    private static readonly IBrush GroupBrush = Brush.Parse("#34495B");
    private static readonly IBrush[] Palette =
    {
        Brush.Parse("#4269C4"), Brush.Parse("#694CB5"),
        Brush.Parse("#247F9A"), Brush.Parse("#2A9B83"),
        Brush.Parse("#9A6340"), Brush.Parse("#B25083"),
        Brush.Parse("#4A84C3"), Brush.Parse("#6D639E"),
        Brush.Parse("#468A63"), Brush.Parse("#9A7638"),
        Brush.Parse("#467AB0"), Brush.Parse("#9E508F")
    };
    private static readonly IPen InnerBorder = new Pen(Brush.Parse("#071925"), 1);
    private static readonly IPen SelectedBorder = new Pen(Brush.Parse("#ECFFFF"), 2.5);
    private static readonly IPen HoverBorder = new Pen(Brush.Parse("#73F6FA"), 2);

    private IReadOnlyList<TreemapEntry> _entries = Array.Empty<TreemapEntry>();
    private IReadOnlyList<TreemapTile> _tiles = Array.Empty<TreemapTile>();
    private Size _layoutSize = default;
    private TreemapTile? _hovered;
    private StorageNode? _directory;
    private StorageNode? _selected;

    public event Action<StorageNode>? NodeSelected;
    public event Action<StorageNode>? DirectoryRequested;
    public event Action<TreemapEntry?>? HoverChanged;

    public StorageTreemapControl()
    {
        Height = 390;
        MinWidth = 170;
        ClipToBounds = true;
        Focusable = true;
        ToolTip.SetShowDelay(this, 350);
    }

    public void ShowDirectory(StorageNode? directory)
    {
        _directory = directory;
        if (directory is null) _selected = null; // Release old scan hierarchy on reset.
        _entries = directory is null
            ? Array.Empty<TreemapEntry>() : TreemapLayout.CreateEntries(directory);
        _layoutSize = default;
        _tiles = Array.Empty<TreemapTile>();
        SetHovered(null);
        InvalidateVisual();
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

    private void EnsureLayout()
    {
        var size = Bounds.Size;
        if (size == _layoutSize)
            return;
        _layoutSize = size;
        _tiles = TreemapLayout.Calculate(_entries,
            new TreemapRect(0, 0, size.Width, size.Height));
        SetHovered(null);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureLayout();
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        foreach (var tile in _tiles)
        {
            var bounds = tile.Rect;
            if (bounds.Width <= 1 || bounds.Height <= 1)
                continue;
            var rect = new Rect(bounds.X + 1, bounds.Y + 1,
                Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2));

            var fill = tile.Entry.IsAggregate ? GroupBrush : ColourFor(tile.Entry.Label);
            context.DrawRectangle(fill, InnerBorder, rect);
            if (tile.Entry.Node is { } node && ReferenceEquals(node, _selected))
                context.DrawRectangle(null, SelectedBorder, rect);
            else if (_hovered is { } hovered && Equals(hovered.Entry, tile.Entry))
                context.DrawRectangle(null, HoverBorder, rect);

            if (rect.Width < 58 || rect.Height < 24)
                continue;

            // Labels are drawn inside the one control; no thousands of
            // TextBlock objects are instantiated for a big scan.
            using (context.PushClip(rect))
            {
                var name = Fit(tile.Entry.Label, rect.Width - 12);
                var label = new FormattedText(
                    name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Inter"), 12, Brushes.White);
                context.DrawText(label, new Point(rect.X + 6, rect.Y + 5));

                if (rect.Height >= 45 && rect.Width >= 80)
                {
                    var sizeText = new FormattedText(
                        Bytes(tile.Entry.LogicalBytes), CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface("Inter"), 11,
                        Brushes.White);
                    context.DrawText(sizeText, new Point(rect.X + 6, rect.Y + 24));
                }
            }
        }

        if (_directory is null)
            DrawHint(context, "Scan a drive to see its treemap.");
        else if (_tiles.Count == 0)
            DrawHint(context, "No measured, nonzero files in this folder.");
    }

    private static void DrawHint(DrawingContext context, string message)
    {
        var text = new FormattedText(message, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Inter"), 13, Brushes.LightGray);
        context.DrawText(text, new Point(12, 16));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        EnsureLayout();
        var point = e.GetPosition(this);
        SetHovered(TreemapLayout.HitTest(_tiles, point.X, point.Y));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        EnsureLayout();
        var point = e.GetPosition(this);
        var hit = TreemapLayout.HitTest(_tiles, point.X, point.Y);
        if (hit?.Entry.Node is not { } node)
            return; // Aggregate tile has no single folder to open.

        NodeSelected?.Invoke(node);
        if (e.ClickCount >= 2 && node.IsDirectory)
            DirectoryRequested?.Invoke(node);
        e.Handled = true;
    }

    private void SetHovered(TreemapTile? tile)
    {
        if (Nullable.Equals(_hovered, tile))
            return;
        _hovered = tile;
        if (tile is { } hover)
        {
            var entry = hover.Entry;
            string location = entry.Node is { } node
                ? node.GetFullPath()
                : $"{entry.GroupedItems:N0} smaller entries grouped";
            ToolTip.SetTip(this, $"{entry.Label}\n{Bytes(entry.LogicalBytes)}\n{location}");
        }
        else
            ToolTip.SetTip(this, null);

        HoverChanged?.Invoke(tile?.Entry);
        InvalidateVisual();
    }

    private static IBrush ColourFor(string label)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in label)
            {
                hash ^= char.ToUpperInvariant(c);
                hash *= 16777619;
            }
            return Palette[(int)(hash % Palette.Length)];
        }
    }

    private static string Fit(string text, double width)
    {
        int count = Math.Max(1, (int)(width / 7.4));
        return text.Length <= count ? text
            : count <= 2 ? text[..count] : text[..(count - 1)] + "…";
    }

    private static string Bytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:N1} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):N1} MiB";
        return $"{bytes / (1024d * 1024 * 1024):N2} GiB";
    }
}
