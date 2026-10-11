using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

// Read-only scanners. No assumptions about the location of a Godot checkout.
public sealed record InspectorRow(string Category, string File, string Detail, long Bytes = 0)
{
    public string Display => Bytes > 0 ? $"{Category}  |  {Bytes / 1024.0 / 1024.0:N2} MiB  |  {File}\n{Detail}"
                                      : $"{Category}  |  {File}\n{Detail}";
}
public static class InspectorServices
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".godot", "bin", "obj", ".vs", "node_modules" };

    private static IEnumerable<string> Files(string folder, CancellationToken cancel)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));
        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            foreach (var item in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                cancel.ThrowIfCancellationRequested();
                var attr = File.GetAttributes(item);
                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                if ((attr & FileAttributes.Directory) != 0)
                {
                    if (!Ignored.Contains(Path.GetFileName(item))) pending.Push(item);
                }
                else yield return item;
            }
        }
    }

    public static Task<List<InspectorRow>> LargestFilesAsync(string folder, int limit, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing folder.");
            return Files(folder, ct).Select(p => new FileInfo(p))
                .OrderByDescending(f => f.Length).Take(limit)
                .Select(f => new InspectorRow("FILE", Path.GetRelativePath(folder, f.FullName),
                    f.Extension + " | Last modified " + f.LastWriteTime.ToString("g"), f.Length)).ToList();
        }, ct);

    public static Task<List<InspectorRow>> CheckResourcesAsync(string folder, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (!File.Exists(Path.Combine(folder, "project.godot")))
                throw new IOException("Select a Godot project folder containing project.godot.");
            var findings = new List<InspectorRow>();
            var link = new Regex(@"res://[^\s""'\)\],;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled,
                TimeSpan.FromSeconds(2));
            foreach (var path in Files(folder, ct))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is not (".tres" or ".tscn" or ".gd" or ".cs" or ".gdshader" or ".cfg")) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException e)
                {
                    findings.Add(new InspectorRow("READ ERROR", Path.GetRelativePath(folder, path), e.Message));
                    continue;
                }
                foreach (Match m in link.Matches(text))
                {
                    ct.ThrowIfCancellationRequested();
                    var reference = Uri.UnescapeDataString(m.Value).Replace('/', Path.DirectorySeparatorChar);
                    var relative = reference.Substring(6);
                    var target = Path.GetFullPath(Path.Combine(folder, relative));
                    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new InspectorRow("ESCAPES PROJECT", Path.GetRelativePath(folder,path), m.Value));
                        continue;
                    }
                    if (!File.Exists(target) && !Directory.Exists(target))
                        findings.Add(new InspectorRow("MISSING RESOURCE", Path.GetRelativePath(folder,path), m.Value));
                }
            }
            if (findings.Count == 0) findings.Add(new InspectorRow("OK", "Project scan completed", "No missing res:// references found in scanned text files."));
            return findings;
        },ct);
}
