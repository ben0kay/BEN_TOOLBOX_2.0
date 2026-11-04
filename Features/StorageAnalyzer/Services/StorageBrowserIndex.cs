using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Services;

/// <summary>
/// Pure, read-only operations over the retained SA1 snapshot. No filesystem
/// enumeration, Avalonia dependencies or per-file UI objects.
/// Work should be scheduled off the UI thread by the window.
/// </summary>
public static class StorageBrowserIndex
{
    public const int PageSize = 80;
    public const int LargestFileLimit = 2500;

    public enum SortKey { Name, Size, Extension }

    public static StorageNode[] ListDirectory(
        StorageNode folder, SortKey sort, bool descending, string? search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!folder.IsDirectory)
            throw new ArgumentException("Browser selection must be a directory.", nameof(folder));
        cancellationToken.ThrowIfCancellationRequested();
        var rows = Filter(folder.Children, search, cancellationToken);
        Sort(rows, sort, descending, groupFoldersFirst: true, token: cancellationToken);
        return rows;
    }

    /// <summary>
    /// Bounded global top-N index: O(files log N) time and O(N + folder depth)
    /// extra memory. Include zero/1KiB files in ranking when eligible; the
    /// treemap's hard visual floor does NOT alter scanner/browser data.
    /// </summary>
    public static StorageNode[] LargestFiles(
        IReadOnlyList<StorageNode> roots, int limit = LargestFileLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        var heap = new PriorityQueue<StorageNode, long>();
        // Cursor-based DFS keeps traversal stack O(folder depth), not
        // O(files in one giant directory). Only top-N references are retained.
        var pending = new Stack<(IReadOnlyList<StorageNode> Children, int Index)>();
        pending.Push((roots, 0));

        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (children, index) = pending.Pop();
            if (index >= children.Count) continue;
            pending.Push((children, index + 1));
            var node = children[index];
            if (node.IsDirectory)
            {
                pending.Push((node.Children, 0));
                continue;
            }

            if (heap.Count < limit)
                heap.Enqueue(node, node.LogicalBytes);
            else if (heap.TryPeek(out _, out long smallest)
                && node.LogicalBytes > smallest)
            {
                heap.Dequeue();
                heap.Enqueue(node, node.LogicalBytes);
            }
        }

        var result = heap.UnorderedItems.Select(x => x.Element).ToArray();
        Sort(result, SortKey.Size, descending: true, groupFoldersFirst: false,
            token: cancellationToken);
        return result;
    }

    public static StorageNode[] SortLargest(
        IReadOnlyList<StorageNode> ranked, SortKey sort, bool descending,
        string? search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = Filter(ranked, search, cancellationToken);
        Sort(rows, sort, descending, groupFoldersFirst: false, token: cancellationToken);
        return rows;
    }

    public static StorageNode[] Page(
        IReadOnlyList<StorageNode> sorted, int zeroBasedPage,
        int pageSize = PageSize)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (zeroBasedPage < 0) throw new ArgumentOutOfRangeException(nameof(zeroBasedPage));
        long offset = (long)zeroBasedPage * pageSize;
        if (offset >= sorted.Count) return Array.Empty<StorageNode>();
        int count = (int)Math.Min((long)sorted.Count - offset, pageSize);
        var result = new StorageNode[count];
        for (int i = 0; i < count; i++)
            result[i] = sorted[(int)offset + i];
        return result;
    }

    private static StorageNode[] Filter(
        IReadOnlyList<StorageNode> nodes, string? query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
            return nodes.ToArray();

        query = query.Trim();
        var result = new List<StorageNode>();
        for (int i = 0; i < nodes.Count; ++i)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if (nodes[i].Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                result.Add(nodes[i]);
        }
        return result.ToArray();
    }

    private static void Sort(
        StorageNode[] nodes, SortKey key, bool descending,
        bool groupFoldersFirst, CancellationToken token)
    {
        int comparisons = 0;
        Array.Sort(nodes, (left, right) =>
        {
            if ((++comparisons & 16383) == 0) token.ThrowIfCancellationRequested();

            if (groupFoldersFirst && left.IsDirectory != right.IsDirectory)
                return left.IsDirectory ? -1 : 1;

            int order = key switch
            {
                SortKey.Size => left.LogicalBytes.CompareTo(right.LogicalBytes),
                SortKey.Extension => StringComparer.OrdinalIgnoreCase.Compare(
                    Path.GetExtension(left.Name), Path.GetExtension(right.Name)),
                _ => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name)
            };
            if (descending) order = -order;
            if (order != 0) return order;

            // Stable, human-friendly secondary order.
            order = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return order == 0 ? StringComparer.Ordinal.Compare(left.Name, right.Name)
                : order;
        });
        token.ThrowIfCancellationRequested();
    }
}
