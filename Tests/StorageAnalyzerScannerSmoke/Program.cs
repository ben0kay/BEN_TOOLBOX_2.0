using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Services;

var root = Path.Combine(Path.GetTempPath(), "BTX-SA1-TEST-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

try
{
    Directory.CreateDirectory(Path.Combine(root, "keep"));
    Directory.CreateDirectory(Path.Combine(root, "node_modules"));
    Directory.CreateDirectory(Path.Combine(root, "Temp"));
    Directory.CreateDirectory(Path.Combine(root, "Templates"));
    File.WriteAllBytes(Path.Combine(root, "keep", "keep.dat"), new byte[123]);
    File.WriteAllBytes(Path.Combine(root, "keep", "skip.tmp"), new byte[300]);
    File.WriteAllBytes(Path.Combine(root, "node_modules", "notCounted.dat"), new byte[321]);
    File.WriteAllBytes(Path.Combine(root, "Temp", "notCounted.dat"), new byte[1024]);
    File.WriteAllBytes(Path.Combine(root, "Templates", "counted.dat"), new byte[17]);

    var options = new StorageScanOptions(
        new[] { root },
        new[] { Path.Combine(root, "Temp") },
        new[] { "node_modules" },
        new[] { "*.tmp" });
    var progressCount = 0;
    var scan = await new StorageScanService().ScanAsync(options, _ => progressCount++,
        CancellationToken.None);

    Assert(!scan.Cancelled, "Normal scan was cancelled");
    Assert(scan.LogicalBytes == 140, $"Expected 140 bytes, got {scan.LogicalBytes}");
    Assert(scan.Files == 2, $"Expected 2 files, got {scan.Files}");
    Assert(scan.Directories == 3, $"Expected 3 discovered directories, got {scan.Directories}");
    Assert(scan.FilteredEntries == 3, "Expected folder, path and extension filters to skip three entries");
    Assert(scan.Roots.Count == 1 && scan.Roots[0].LogicalBytes == 140,
        "Root size aggregation incorrect");
    Assert(scan.Roots[0].FileCount == 2 && scan.Roots[0].SubdirectoryCount == 2,
        "Recursive file and directory aggregation incorrect");
    var child = scan.Roots[0].Children.Single(x => x.Name == "keep");
    Assert(Path.GetFullPath(child.GetFullPath()) == Path.GetFullPath(Path.Combine(root, "keep")),
        "Node path reconstruction incorrect");
    Assert(progressCount > 0, "Progress was not reported");

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    var partial = await new StorageScanService().ScanAsync(options, null, cancelled.Token);
    Assert(partial.Cancelled, "Pre-cancelled scan should produce a cancelled partial snapshot");

    try
    {
        await new StorageScanService().ScanAsync(
            options with { Roots = new[] { root, Path.Combine(root, "keep") } }, null,
            CancellationToken.None);
        throw new Exception("Overlapping roots should have been rejected");
    }
    catch (ArgumentException) { }

    Console.WriteLine("PASS: metadata counts, root aggregation, path exclusions, " +
        "directory filters, extensions, cancellation and root overlap protection.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
