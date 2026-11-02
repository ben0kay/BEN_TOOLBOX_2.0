using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Services;

/// <summary>
/// Immutable, precompiled exclusions applied BEFORE pushing directories onto
/// the traversal stack. No Windows-only APIs or Avalonia dependencies.
/// </summary>
internal sealed class ScanExclusionMatcher
{
    private readonly string[] paths;
    private readonly string[] names;
    private readonly string[] extensions;
    private readonly StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public ScanExclusionMatcher(StorageScanOptions options)
    {
        paths = options.ExcludedPaths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Canonical(x.Trim()))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        names = options.ExcludedDirectoryNames
            .Select(x => x.Trim())
            .Where(x => x.Length != 0)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        extensions = options.ExcludedExtensions
            .Select(NormalizeExtension)
            .Where(x => x.Length != 0)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
    }

    public static string Canonical(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public bool MatchesExcludedPath(string absolutePath)
    {
        if (paths.Length == 0)
            return false;
        // FileSystemInfo.FullName is already fully qualified. Avoid the
        // relatively expensive Path.GetFullPath for every scanned file.
        var candidate = Path.TrimEndingDirectorySeparator(absolutePath);
        foreach (var excluded in paths)
        {
            if (string.Equals(candidate, excluded, comparison))
                return true;

            // Component boundary prevents C:\Temp from also hiding C:\Templates.
            var prefix = Path.EndsInDirectorySeparator(excluded)
                ? excluded : excluded + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(prefix, comparison))
                return true;
        }
        return false;
    }

    public bool MatchesDirectory(string fullPath, string leafName)
    {
        if (MatchesExcludedPath(fullPath))
            return true;
        foreach (var pattern in names)
            if (FileSystemName.MatchesSimpleExpression(pattern, leafName,
                    ignoreCase: OperatingSystem.IsWindows()))
                return true;
        return false;
    }

    public bool MatchesFile(string fullPath, string leafName)
    {
        if (MatchesExcludedPath(fullPath))
            return true;
        var extension = Path.GetExtension(leafName);
        return extensions.Any(x => string.Equals(x, extension, comparison));
    }

    private static string NormalizeExtension(string value)
    {
        var v = value.Trim();
        if (v.StartsWith("*.", StringComparison.Ordinal))
            v = v[1..];
        else if (v.Length > 0 && v[0] != '.')
            v = "." + v;
        // Only extensions, not arbitrary filename or path glob patterns.
        return v is "." ? "" : v;
    }
}
