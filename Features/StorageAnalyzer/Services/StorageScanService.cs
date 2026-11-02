using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Services;

/// <summary>
/// Read-only metadata traversal. No Avalonia reference, file content reads,
/// Windows APIs, hashing or deletion. Task.Run keeps UI free during traversal.
/// </summary>
public sealed class StorageScanService
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,   // count/report rather than silently hiding
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    public Task<StorageScanResult> ScanAsync(
        StorageScanOptions options,
        Action<StorageScanProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Deliberately do not pass token to Task.Run: even a pre-cancelled
        // request returns a labelled partial/cancelled snapshot instead of
        // losing all discovered information via TaskCanceledException.
        return Task.Run(() => Scan(options, onProgress, cancellationToken));
    }

    private static StorageScanResult Scan(
        StorageScanOptions options,
        Action<StorageScanProgress>? progress,
        CancellationToken token)
    {
        var matcher = new ScanExclusionMatcher(options);
        var canonicalRoots = NormalizeRoots(options.Roots);
        foreach (var root in canonicalRoots)
        {
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Selected scan root is missing or unavailable: {root}");
            // Drive roots may themselves be mounted via a reparse point.
            // Do not bypass the no-follow safety rule for the starting node.
            if ((new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Refusing to follow a reparse-point scan root: {root}");
            if (matcher.MatchesExcludedPath(root))
                throw new ArgumentException($"Selected scan root is excluded: {root}");
        }

        var elapsed = Stopwatch.StartNew();
        var roots = new List<StorageNode>(canonicalRoots.Count);
        var allDirectories = new List<StorageNode>();
        var stack = new Stack<(string Path, StorageNode Node)>();
        var errorSamples = new List<string>(24);

        long files = 0;
        long folders = 0;
        long bytes = 0;
        long filtered = 0;
        long reparse = 0;
        long inaccessible = 0;
        long lastReportMs = -250;

        void Report(string path, bool force = false)
        {
            if (progress is null)
                return;
            var milliseconds = elapsed.ElapsedMilliseconds;
            if (!force && milliseconds - lastReportMs < 250)
                return;
            lastReportMs = milliseconds;
            progress(new StorageScanProgress(files, folders, bytes, filtered,
                reparse, inaccessible, elapsed.Elapsed, path));
        }

        void Error(string path, Exception e)
        {
            inaccessible++;
            if (errorSamples.Count < 24)
                errorSamples.Add($"{path}: {e.GetType().Name} — {e.Message}");
        }

        foreach (var root in canonicalRoots)
        {
            var node = new StorageNode(root, isDirectory: true, parent: null);
            roots.Add(node);
            allDirectories.Add(node);
            stack.Push((root, node));
            folders++;
        }

        Report(canonicalRoots[0], force: true);
        while (stack.Count != 0 && !token.IsCancellationRequested)
        {
            var (currentPath, parent) = stack.Pop();
            // Enumeration is explicit single-level; recursion is controlled by
            // our stack. Only matching, non-reparse directories are pushed.
            try
            {
                using var entries = new DirectoryInfo(currentPath)
                    .EnumerateFileSystemInfos("*", Enumeration).GetEnumerator();

                while (!token.IsCancellationRequested)
                {
                    FileSystemInfo entry;
                    try
                    {
                        if (!entries.MoveNext())
                            break;
                        entry = entries.Current;
                    }
                    catch (Exception e) when (IsFilesystemError(e))
                    {
                        Error(currentPath, e);
                        // Some enumerators cannot continue after an access
                        // failure; keep scanning other queued directories.
                        break;
                    }

                    try
                    {
                        var attributes = entry.Attributes;
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            reparse++;
                            continue;
                        }

                        if (!options.IncludeHiddenAndSystem
                            && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            filtered++;
                            continue;
                        }

                        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                        if (isDirectory)
                        {
                            if (matcher.MatchesDirectory(entry.FullName, entry.Name))
                            {
                                filtered++;
                                continue;
                            }
                            var child = new StorageNode(entry.Name, true, parent);
                            parent.AddChild(child);
                            allDirectories.Add(child);
                            stack.Push((entry.FullName, child));
                            folders++;
                        }
                        else
                        {
                            if (matcher.MatchesFile(entry.FullName, entry.Name))
                            {
                                filtered++;
                                continue;
                            }

                            long length = entry is FileInfo info
                                ? info.Length : new FileInfo(entry.FullName).Length;
                            var file = new StorageNode(entry.Name, false, parent)
                            {
                                LogicalBytes = length,
                                FileCount = 1
                            };
                            parent.AddChild(file);
                            parent.LogicalBytes += length;
                            parent.FileCount++;
                            bytes += length;
                            files++;
                        }
                    }
                    catch (Exception e) when (IsFilesystemError(e))
                    {
                        Error(entry.FullName, e);
                    }

                    Report(currentPath);
                }
            }
            catch (Exception e) when (IsFilesystemError(e))
            {
                Error(currentPath, e);
            }
            Report(currentPath);
        }

        // Bottom-up accumulation is O(directories) and does not recurse.
        // Direct file bytes/counts were already accumulated during traversal.
        for (int i = allDirectories.Count - 1; i >= 0; --i)
        {
            var dir = allDirectories[i];
            if (dir.Parent is not { } parent)
                continue;
            parent.LogicalBytes += dir.LogicalBytes;
            parent.FileCount += dir.FileCount;
            parent.SubdirectoryCount += 1 + dir.SubdirectoryCount;
        }

        elapsed.Stop();
        Report(stack.Count == 0 ? canonicalRoots[0] : stack.Peek().Path, force: true);
        return new StorageScanResult(roots, files, folders, bytes,
            filtered, reparse, inaccessible, elapsed.Elapsed,
            token.IsCancellationRequested, errorSamples.ToArray());
    }

    private static IReadOnlyList<string> NormalizeRoots(IReadOnlyList<string> requested)
    {
        if (requested is null || requested.Count == 0)
            throw new ArgumentException("Select a drive before starting a scan.");
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var roots = requested.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ScanExclusionMatcher.Canonical(x.Trim()))
            .Distinct(comparer).ToArray();
        if (roots.Length == 0)
            throw new ArgumentException("Select a drive before starting a scan.");

        // Future multi-root support must never double-count nested roots.
        for (int i = 0; i < roots.Length; ++i)
            for (int j = i + 1; j < roots.Length; ++j)
                if (IsWithin(roots[i], roots[j], comparer)
                    || IsWithin(roots[j], roots[i], comparer))
                    throw new ArgumentException("Scan roots overlap; select non-overlapping drives.");
        return roots;
    }

    private static bool IsWithin(string child, string parent, StringComparer comparer)
    {
        if (comparer.Equals(child, parent))
            return true;
        string prefix = Path.EndsInDirectorySeparator(parent)
            ? parent : parent + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return child.StartsWith(prefix, comparison);
    }

    private static bool IsFilesystemError(Exception e) =>
        e is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException;
}
