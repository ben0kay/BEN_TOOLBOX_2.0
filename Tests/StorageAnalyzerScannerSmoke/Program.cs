using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Treemap;
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


    // SA2: Pure layout checks do not require Avalonia or a scanned drive.
    var fakeRoot = new StorageNode("virtual-root", true, null);
    long expected = 0;
    for (int i = 0; i < 100; i++)
    {
        long amount = 100 - i;
        var node = new StorageNode("file-" + i, false, fakeRoot)
        {
            LogicalBytes = amount, FileCount = 1
        };
        fakeRoot.AddChild(node);
        expected += amount;
    }
    fakeRoot.LogicalBytes = expected;
    var entries = TreemapLayout.CreateEntries(fakeRoot, maxTiles: 10);
    Assert(entries.Count == 10, "Top-k should include 9 items plus one Other aggregate");
    Assert(entries.Count(x => x.IsAggregate) == 1, "Expected one Other aggregate");
    Assert(entries.Single(x => x.IsAggregate).GroupedItems == 91,
        "The Other tile must represent all remaining entries");
    Assert(entries.Sum(x => x.LogicalBytes) == expected,
        "The treemap must preserve every measured byte, including Other");

    foreach (var viewport in new[]
    {
        new TreemapRect(0, 0, 1000, 500),
        new TreemapRect(0, 0, 280, 650),
        new TreemapRect(0, 0, 86, 68)
    })
    {
        var tiles = TreemapLayout.Calculate(entries, viewport);
        Assert(tiles.Count > 0 && tiles.Count <= 10,
            "Treemap must remain bounded after viewport reduction");
        Assert(Math.Abs(tiles.Sum(x => x.Rect.Area) - viewport.Area) < 0.002,
            "Treemap should fill the complete viewport, even when compacted");
        for (int i = 0; i < tiles.Count; i++)
        {
            var a = tiles[i].Rect;
            Assert(a.Width >= 0 && a.Height >= 0
                && a.X >= -0.001 && a.Y >= -0.001
                && a.X + a.Width <= viewport.Width + 0.001
                && a.Y + a.Height <= viewport.Height + 0.001,
                "A treemap tile escaped the layout boundary");
            var hit = TreemapLayout.HitTest(tiles,
                a.X + a.Width / 2, a.Y + a.Height / 2);
            Assert(hit is not null && Equals(hit.Value.Entry, tiles[i].Entry),
                "Centre-point hit test returned the wrong tile");
            for (int j = i + 1; j < tiles.Count; j++)
            {
                var b = tiles[j].Rect;
                double overlapWidth = Math.Max(0,
                    Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
                double overlapHeight = Math.Max(0,
                    Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
                Assert(overlapWidth * overlapHeight < 0.001,
                    "Treemap tiles overlap");
            }
        }
    }
    Assert(TreemapLayout.Calculate(Array.Empty<TreemapEntry>(),
        new TreemapRect(0, 0, 500, 300)).Count == 0,
        "Empty entries should draw nothing");
    Assert(TreemapLayout.Calculate(entries,
        new TreemapRect(0, 0, 0, 300)).Count == 0,
        "Zero-area viewport should draw nothing");
    Assert(TreemapLayout.HitTest(Array.Empty<TreemapTile>(), 1, 1) is null,
        "Empty hit test should not select anything");

    Console.WriteLine("PASS: SA2 proportional squarified layout, top-k bounds, Other totals, " +
        "portrait/landscape/tiny resizing, geometry and hit tests.");

    Console.WriteLine("PASS: metadata counts, root aggregation, path exclusions, " +
        "directory filters, extensions, cancellation and root overlap protection.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
