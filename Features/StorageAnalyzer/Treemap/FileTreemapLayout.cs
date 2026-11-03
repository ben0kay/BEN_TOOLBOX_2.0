using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Treemap;

public readonly record struct FileTreemapTile(StorageNode File, TreemapRect Rect);
public readonly record struct FolderTreemapTile(StorageNode Folder, TreemapRect Rect, int Depth);

/// <summary>
/// Immutable output of the background treemap build. Each positive-sized file
/// meeting the user threshold has its own geometric rectangle (no 96-tile cap).
/// Pixel indices reflect what can actually be distinguished at this zoom.
/// </summary>
public sealed record FileTreemapSnapshot(
    int Width, int Height, byte[] BgraPixels, int[] PixelFileIndices,
    IReadOnlyList<FileTreemapTile> Files, IReadOnlyList<FolderTreemapTile> Folders,
    IReadOnlyDictionary<StorageNode, int> FileIndex,
    long VisibleBytes, long HiddenBytes, long VisibleFiles,
    long HiddenFiles, long ZeroByteFiles, long MinimumFileBytes)
{
    public FileTreemapTile? HitTest(double x, double y)
    {
        int px = (int)Math.Floor(x), py = (int)Math.Floor(y);
        if (px < 0 || py < 0 || px >= Width || py >= Height)
            return null;
        int index = PixelFileIndices[py * Width + px];
        return index >= 0 && index < Files.Count ? Files[index] : null;
    }
}

/// <summary>
/// File-level, folder-nested squarified treemap and software rasterizer.
/// Build off the UI thread, then upload a single bitmap on the UI thread.
/// Thresholds affect visualization ONLY; scanned StorageNode totals never change.
/// </summary>
public static class FileTreemapLayout
{
    // Opaque BGRA palette; files with the same extension get the same colour.
    private static readonly uint[] Palette =
    {
        0xFFB65EAD, 0xFF4F89C8, 0xFF438C75, 0xFFB18A48,
        0xFF835FCC, 0xFF2DB8AD, 0xFFA36854, 0xFF6784C9,
        0xFFBC668C, 0xFF528FB0, 0xFF54866B, 0xFF916FAD
    };
    private const uint Background = 0xFF061B28; // RGB #061B28, stored as BGRA bytes
    private const uint Outline = 0xFF172F3F;

    public static FileTreemapSnapshot Build(
        StorageNode directory, int width, int height,
        long minimumFileBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!directory.IsDirectory)
            throw new ArgumentException("Treemap root must be a directory.");
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid treemap viewport.");
        if (minimumFileBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumFileBytes));

        cancellationToken.ThrowIfCancellationRequested();
        var folders = new List<StorageNode>();
        var nodes = new Stack<StorageNode>();
        nodes.Push(directory);
        long visibleFiles = 0, hiddenFiles = 0, zeroFiles = 0;
        long hiddenBytes = 0;

        // Iterative traversal, so deeply nested user folders cannot overflow
        // the managed call stack. Only directory subtotal values are cached.
        while (nodes.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes.Pop();
            if (node.IsDirectory)
            {
                folders.Add(node);
                foreach (var child in node.Children)
                    nodes.Push(child);
            }
            else if (node.LogicalBytes == 0)
                zeroFiles++;
            else if (node.LogicalBytes < minimumFileBytes)
            {
                hiddenFiles++;
                hiddenBytes += node.LogicalBytes;
            }
            else
                visibleFiles++;
        }

        var visibleDirectoryBytes = new Dictionary<StorageNode, long>(folders.Count);
        for (int i = folders.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = folders[i];
            long sum = 0;
            foreach (var child in dir.Children)
                sum += child.IsDirectory
                    ? visibleDirectoryBytes[child]
                    : child.LogicalBytes >= minimumFileBytes ? child.LogicalBytes : 0;
            visibleDirectoryBytes[dir] = sum;
        }

        long visibleBytes = visibleDirectoryBytes[directory];
        var pixels = new byte[checked(width * height * 4)];
        var hits = new int[checked(width * height)];
        Array.Fill(hits, -1);
        FillBackground(pixels, Background);

        var fileTiles = new List<FileTreemapTile>();
        var fileIndex = new Dictionary<StorageNode, int>();
        var folderTiles = new List<FolderTreemapTile>(folders.Count);

        if (visibleBytes > 0)
        {
            var pending = new Stack<(StorageNode Folder, TreemapRect Rect, int Depth)>();
            pending.Push((directory, new TreemapRect(0, 0, width, height), 0));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (folder, area, depth) = pending.Pop();
                folderTiles.Add(new FolderTreemapTile(folder, area, depth));

                var entries = new List<TreemapEntry>(folder.Children.Count);
                foreach (var child in folder.Children)
                {
                    long weight = child.IsDirectory ? visibleDirectoryBytes[child]
                        : child.LogicalBytes >= minimumFileBytes ? child.LogicalBytes : 0;
                    if (weight > 0)
                        entries.Add(new TreemapEntry(child, child.Name, weight));
                }

                // Zero grouping: every eligible child receives its own area.
                // Nested folder areas are subdivided into their individual
                // files; this is a true file-level treemap, not a folder map.
                var rectangles = TreemapLayout.Calculate(
                    entries, area, minTileArea: 0);
                foreach (var tile in rectangles)
                {
                    var child = tile.Entry.Node!;
                    if (child.IsDirectory)
                        pending.Push((child, tile.Rect, depth + 1));
                    else
                    {
                        int index = fileTiles.Count;
                        fileTiles.Add(new FileTreemapTile(child, tile.Rect));
                        fileIndex.Add(child, index);
                        PaintFile(pixels, hits, width, height, tile.Rect,
                            ColourFor(child.Name), index);
                    }
                }
            }

            // Top-level folder outlines help retain the WinDirStat visual
            // grouping. Skip unreadably small boxes and deep nested borders.
            foreach (var folder in folderTiles)
            {
                if (folder.Depth == 0 || folder.Depth > 3
                    || folder.Rect.Width < 22 || folder.Rect.Height < 16)
                    continue;
                DrawOutline(pixels, width, height, folder.Rect, Outline);
            }
        }

        return new FileTreemapSnapshot(width, height, pixels, hits,
            fileTiles, folderTiles, fileIndex, visibleBytes, hiddenBytes,
            visibleFiles, hiddenFiles, zeroFiles, minimumFileBytes);
    }

    private static void FillBackground(byte[] pixels, uint bgra)
    {
        for (int p = 0; p < pixels.Length; p += 4)
        {
            pixels[p] = (byte)bgra;
            pixels[p + 1] = (byte)(bgra >> 8);
            pixels[p + 2] = (byte)(bgra >> 16);
            pixels[p + 3] = 255;
        }
    }

    private static void PaintFile(byte[] pixels, int[] hits, int width, int height,
        TreemapRect rect, uint colour, int fileIndex)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // Every file has an exact geometrical rectangle. Below one screen
        // pixel multiple rectangles necessarily compete for the same pixel.
        // Paint their shared pixel to create a dense detail texture, while
        // retaining every file in the underlying hit/layout model.
        int x0 = Math.Clamp((int)Math.Floor(rect.X), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor(rect.Y), 0, height - 1);
        int x1 = Math.Clamp((int)Math.Ceiling(rect.X + rect.Width), x0 + 1, width);
        int y1 = Math.Clamp((int)Math.Ceiling(rect.Y + rect.Height), y0 + 1, height);
        for (int y = y0; y < y1; y++)
        {
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                int p = (row + x) * 4;
                pixels[p] = (byte)colour;
                pixels[p + 1] = (byte)(colour >> 8);
                pixels[p + 2] = (byte)(colour >> 16);
                pixels[p + 3] = 255;
                hits[row + x] = fileIndex;
            }
        }
    }

    private static void DrawOutline(byte[] pixels, int width, int height,
        TreemapRect rect, uint colour)
    {
        int x0 = Math.Clamp((int)Math.Floor(rect.X), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor(rect.Y), 0, height - 1);
        int x1 = Math.Clamp((int)Math.Ceiling(rect.X + rect.Width) - 1, 0, width - 1);
        int y1 = Math.Clamp((int)Math.Ceiling(rect.Y + rect.Height) - 1, 0, height - 1);
        if (x1 <= x0 || y1 <= y0) return;

        for (int x = x0; x <= x1; x++)
        {
            SetPixel(pixels, width, x, y0, colour);
            SetPixel(pixels, width, x, y1, colour);
        }
        for (int y = y0; y <= y1; y++)
        {
            SetPixel(pixels, width, x0, y, colour);
            SetPixel(pixels, width, x1, y, colour);
        }
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, uint bgra)
    {
        int i = (y * width + x) * 4;
        pixels[i] = (byte)bgra;
        pixels[i + 1] = (byte)(bgra >> 8);
        pixels[i + 2] = (byte)(bgra >> 16);
        pixels[i + 3] = 255;
    }

    private static uint ColourFor(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in ext)
            {
                hash ^= char.ToUpperInvariant(c);
                hash *= 16777619;
            }
            return Palette[(int)(hash % (uint)Palette.Length)];
        }
    }
}
