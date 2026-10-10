using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record DuplicateMatch(string Source, string Destination, long SizeA, long SizeB, string Status)
{
    public bool Identical => Status == "IDENTICAL";
    public string Description => $"{Status}  |  {Path.GetFileName(Source)}\nA: {Source}\nB: {Destination}\n{SizeA:N0} / {SizeB:N0} bytes";
}
public sealed record ScanResult(List<DuplicateMatch> Rows, int SourceCount, int DestinationCount);

public sealed class DuplicateScanService
{
    public static void CheckRoots(string a, string b)
    {
        if (!Directory.Exists(a) || !Directory.Exists(b)) throw new IOException("Choose two existing folders.");
        a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source and destination folders must not overlap.");
    }
    private static IEnumerable<string> Walk(string path, bool recurse, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count != 0)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                ct.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (recurse) stack.Push(item);
                }
                else yield return item;
            }
        }
    }
    private static string Hash(string path, Dictionary<string,string> cache)
    {
        if (!cache.TryGetValue(path, out var hash))
        {
            using var stream = File.OpenRead(path);
            hash = Convert.ToHexString(SHA256.HashData(stream));
            cache[path] = hash;
        }
        return hash;
    }
    public Task<ScanResult> ScanAsync(string a, string b, bool recurseA, bool recurseB, bool verify, CancellationToken token) =>
        Task.Run(() =>
        {
            CheckRoots(a,b);
            var index = new Dictionary<string,List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            var countB = 0;
            foreach (var path in Walk(b,recurseB,token))
            {
                var info = new FileInfo(path);
                if (!index.TryGetValue(info.Name,out var list)) index[info.Name] = list = new();
                list.Add(info); countB++;
            }
            var rows = new List<DuplicateMatch>(); var cache = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var countA = 0;
            foreach (var path in Walk(a,recurseA,token))
            {
                countA++;
                var info = new FileInfo(path);
                if (!index.TryGetValue(info.Name,out var list)) continue;
                foreach (var other in list)
                {
                    token.ThrowIfCancellationRequested();
                    var status = "NAME MATCH";
                    if (verify) status = info.Length == other.Length && Hash(info.FullName,cache) == Hash(other.FullName,cache)
                        ? "IDENTICAL" : "DIFFERENT CONTENT";
                    rows.Add(new(info.FullName,other.FullName,info.Length,other.Length,status));
                }
            }
            return new ScanResult(rows,countA,countB);
        },token);
    private static bool Within(string file,string folder) =>
        Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    public static bool VerifyAgain(DuplicateMatch match,string a,string b)
    {
        if (!match.Identical || !Within(match.Source,a) || !Within(match.Destination,b) ||
            !File.Exists(match.Source) || !File.Exists(match.Destination)) return false;
        if ((File.GetAttributes(match.Source) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(match.Destination) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(match.Source).Length != new FileInfo(match.Destination).Length) return false;
        return Hash(match.Source,new()) == Hash(match.Destination,new());
    }
}
