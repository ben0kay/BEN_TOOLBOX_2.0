using System;
using System.Collections.Generic;
using System.IO;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;

/// <summary>
/// One or more requested roots are represented for forward compatibility. The
/// SA1 UI deliberately submits only one selected drive at a time.
/// Exclusions describe metadata traversal, not cleanup/deletion.
/// </summary>
public sealed record StorageScanOptions(
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> ExcludedPaths,
    IReadOnlyList<string> ExcludedDirectoryNames,
    IReadOnlyList<string> ExcludedExtensions,
    bool IncludeHiddenAndSystem = true);

/// <summary>
/// Compact hierarchy for SA2 treemap and SA3 inspection. Children keep only
/// their name and parent reference, avoiding a duplicate full path per file.
/// Files have no allocated children list.
/// </summary>
public sealed class StorageNode
{
    private List<StorageNode>? _children;

    internal StorageNode(string name, bool isDirectory, StorageNode? parent)
    {
        Name = name;
        IsDirectory = isDirectory;
        Parent = parent;
    }

    public string Name { get; }
    public bool IsDirectory { get; }
    public StorageNode? Parent { get; }
    public long LogicalBytes { get; internal set; }
    public long FileCount { get; internal set; }
    public long SubdirectoryCount { get; internal set; }
    public IReadOnlyList<StorageNode> Children => _children ?? (IReadOnlyList<StorageNode>)Array.Empty<StorageNode>();

    internal void AddChild(StorageNode child) => (_children ??= new List<StorageNode>()).Add(child);

    /// <summary>Reconstruct only when selected; never allocate full paths for the entire scan.</summary>
    public string GetFullPath()
    {
        var names = new Stack<string>();
        for (StorageNode? node = this; node is not null; node = node.Parent)
            names.Push(node.Name);
        var result = names.Pop(); // Root node stores its canonical absolute path.
        while (names.Count > 0)
            result = Path.Combine(result, names.Pop());
        return result;
    }
}

public sealed record StorageScanProgress(
    long Files,
    long Directories,
    long LogicalBytes,
    long FilteredEntries,
    long ReparseEntries,
    long InaccessibleEntries,
    TimeSpan Elapsed,
    string CurrentDirectory);

/// <summary>
/// LogicalBytes excludes filtered and inaccessible entries; it is NOT physical
/// disk allocation and need not equal drive used-space statistics.
/// </summary>
public sealed record StorageScanResult(
    IReadOnlyList<StorageNode> Roots,
    long Files,
    long Directories,
    long LogicalBytes,
    long FilteredEntries,
    long ReparseEntries,
    long InaccessibleEntries,
    TimeSpan Elapsed,
    bool Cancelled,
    IReadOnlyList<string> ErrorSamples);
