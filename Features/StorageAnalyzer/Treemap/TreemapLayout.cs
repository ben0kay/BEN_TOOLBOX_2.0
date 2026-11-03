using System;
using System.Collections.Generic;
using System.Linq;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Treemap;

/// <summary>Renderer-independent treemap geometry, suitable for headless tests.</summary>
public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public double Area => Math.Max(0, Width) * Math.Max(0, Height);
    public bool Contains(double x, double y) =>
        x >= X && y >= Y && x < X + Width && y < Y + Height;
}

public sealed record TreemapEntry(
    StorageNode? Node, string Label, long LogicalBytes, int GroupedItems = 0)
{
    public bool IsAggregate => Node is null;
}

public readonly record struct TreemapTile(TreemapEntry Entry, TreemapRect Rect);

/// <summary>
/// Squarified layout: draw at most MaxTiles shapes per directory even for a
/// folder with millions of children. The discarded tail is represented as one
/// exact-size "Other" aggregate, never silently omitted.
/// </summary>
public static class TreemapLayout
{
    public const int MaxTiles = 96;
    public const double MinTileArea = 20.0;

    public static IReadOnlyList<TreemapEntry> CreateEntries(
        StorageNode directory, int maxTiles = MaxTiles)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!directory.IsDirectory || maxTiles < 2)
            return Array.Empty<TreemapEntry>();

        // O(n log k) top-k selection, not O(n log n) sorting or one UI child
        // per scanned file. The final treemap contains at most k entries.
        var top = new PriorityQueue<StorageNode, long>();
        long remainderBytes = 0;
        int remainderCount = 0;
        foreach (var node in directory.Children)
        {
            if (node.LogicalBytes <= 0)
                continue;
            if (top.Count < maxTiles - 1)
            {
                top.Enqueue(node, node.LogicalBytes);
            }
            else if (top.TryPeek(out var smallest, out var smallestBytes)
                     && node.LogicalBytes > smallestBytes)
            {
                var removed = top.Dequeue();
                remainderBytes = checked(remainderBytes + removed.LogicalBytes);
                remainderCount++;
                top.Enqueue(node, node.LogicalBytes);
            }
            else
            {
                remainderBytes = checked(remainderBytes + node.LogicalBytes);
                remainderCount++;
            }
        }

        var entries = top.UnorderedItems
            .Select(x => new TreemapEntry(x.Element, x.Element.Name, x.Element.LogicalBytes))
            .OrderByDescending(x => x.LogicalBytes)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (remainderCount > 0)
            entries.Add(new TreemapEntry(null, $"Other ({remainderCount:N0} items)",
                remainderBytes, remainderCount));

        return entries.OrderByDescending(x => x.LogicalBytes).ToArray();
    }

    public static IReadOnlyList<TreemapTile> Calculate(
        IReadOnlyList<TreemapEntry> entries, TreemapRect viewport,
        double minTileArea = MinTileArea)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return Array.Empty<TreemapTile>();

        var positive = entries.Where(x => x.LogicalBytes > 0).ToArray();
        double total = positive.Sum(x => (double)x.LogicalBytes);
        if (total <= 0)
            return Array.Empty<TreemapTile>();

        // Merge imperceptible rectangles into one honest aggregate instead
        // of rendering 0.1-pixel strips or losing their measured bytes.
        var keep = new List<TreemapEntry>(positive.Length);
        long smallBytes = 0;
        int smallCount = 0;
        foreach (var entry in positive)
        {
            if (entry.LogicalBytes * (viewport.Area / total) < minTileArea)
            {
                smallBytes = checked(smallBytes + entry.LogicalBytes);
                smallCount += entry.GroupedItems > 0 ? entry.GroupedItems : 1;
            }
            else
                keep.Add(entry);
        }
        if (smallCount > 0)
            keep.Add(new TreemapEntry(null, $"Other ({smallCount:N0} items)",
                smallBytes, smallCount));

        keep.Sort((a, b) => b.LogicalBytes.CompareTo(a.LogicalBytes));
        var weighted = keep
            .Select(x => new Weighted(x, viewport.Area * (x.LogicalBytes / total)))
            .ToArray();
        var result = new List<TreemapTile>(weighted.Length);
        var row = new List<Weighted>();
        var left = viewport;
        var index = 0;

        while (index < weighted.Length && left.Width > 0 && left.Height > 0)
        {
            var next = weighted[index];
            if (row.Count == 0 ||
                WorstAspect(row, next, Math.Min(left.Width, left.Height))
                    <= WorstAspect(row, null, Math.Min(left.Width, left.Height)))
            {
                row.Add(next);
                index++;
            }
            else
            {
                left = PlaceRow(row, left, result, last: false);
                row.Clear();
            }
        }
        if (row.Count > 0 && left.Width > 0 && left.Height > 0)
            PlaceRow(row, left, result, last: true);
        return result;
    }

    public static TreemapTile? HitTest(
        IReadOnlyList<TreemapTile> tiles, double x, double y)
    {
        foreach (var tile in tiles)
            if (tile.Rect.Contains(x, y))
                return tile;
        return null;
    }

    private readonly record struct Weighted(TreemapEntry Entry, double Area);

    private static double WorstAspect(
        IReadOnlyList<Weighted> row, Weighted? additional, double side)
    {
        if (side <= 0)
            return double.PositiveInfinity;
        double sum = 0;
        double smallest = double.PositiveInfinity;
        double largest = 0;
        foreach (var item in row)
        {
            sum += item.Area;
            smallest = Math.Min(smallest, item.Area);
            largest = Math.Max(largest, item.Area);
        }
        if (additional is { } next)
        {
            sum += next.Area;
            smallest = Math.Min(smallest, next.Area);
            largest = Math.Max(largest, next.Area);
        }
        if (sum <= 0 || smallest <= 0)
            return double.PositiveInfinity;
        double sideSquared = side * side;
        return Math.Max(sideSquared * largest / (sum * sum),
            (sum * sum) / (sideSquared * smallest));
    }

    private static TreemapRect PlaceRow(
        IReadOnlyList<Weighted> row, TreemapRect remaining,
        List<TreemapTile> result, bool last)
    {
        double area = row.Sum(x => x.Area);
        if (remaining.Width >= remaining.Height)
        {
            // Vertical strip on a landscape rectangle.
            double strip = last ? remaining.Width
                : Math.Min(remaining.Width, area / remaining.Height);
            if (strip <= 0) return remaining;
            double y = remaining.Y;
            for (int i = 0; i < row.Count; i++)
            {
                double h = i == row.Count - 1 ? remaining.Y + remaining.Height - y
                    : Math.Min(remaining.Y + remaining.Height - y, row[i].Area / strip);
                result.Add(new TreemapTile(row[i].Entry,
                    new TreemapRect(remaining.X, y, strip, Math.Max(0, h))));
                y += h;
            }
            return new TreemapRect(remaining.X + strip, remaining.Y,
                Math.Max(0, remaining.Width - strip), remaining.Height);
        }
        else
        {
            // Horizontal strip on a portrait rectangle.
            double strip = last ? remaining.Height
                : Math.Min(remaining.Height, area / remaining.Width);
            if (strip <= 0) return remaining;
            double x = remaining.X;
            for (int i = 0; i < row.Count; i++)
            {
                double w = i == row.Count - 1 ? remaining.X + remaining.Width - x
                    : Math.Min(remaining.X + remaining.Width - x, row[i].Area / strip);
                result.Add(new TreemapTile(row[i].Entry,
                    new TreemapRect(x, remaining.Y, Math.Max(0, w), strip)));
                x += w;
            }
            return new TreemapRect(remaining.X, remaining.Y + strip,
                remaining.Width, Math.Max(0, remaining.Height - strip));
        }
    }
}
