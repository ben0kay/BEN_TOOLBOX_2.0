using BEN_TOOLBOX_2_0.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Cryptography;

var root = Path.Combine(Path.GetTempPath(), "PixelArtSmoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var path = Path.Combine(root, "source.png");
    using (var source = new Image<Rgba32>(1000, 1000))
    {
        for (int y = 0; y < 1000; y++)
            for (int x = 0; x < 1000; x++) source[x, y] = new Rgba32((byte)(x % 256), (byte)(y % 256), 70, 255);
        source.SaveAsPng(path);
    }
    var hash = SHA256.HashData(File.ReadAllBytes(path));
    foreach (int width in new[] { 32, 64, 96, 128, 256 })
    {
        using var output = Image.Load<Rgba32>(PixelArtService.Preview(path, new(width), CancellationToken.None));
        if (output.Width != 1000 || output.Height != 1000) throw new Exception("Canvas dimensions changed.");
        if (!output[0, 0].Equals(output[1, 1])) throw new Exception("Logical pixels were smoothed during enlargement.");
    }
    if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Original changed.");
    using (var transparent = new Image<Rgba32>(1200, 800)) transparent.SaveAsPng(path);
    using (var output = Image.Load<Rgba32>(PixelArtService.Preview(path, new(64), CancellationToken.None)))
        if (output.Width != 1200 || output.Height != 800 || output[600, 400].A != 0)
            throw new Exception("Non-square canvas/alpha failure.");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { PixelArtService.Preview(path, new(64), cancellation.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
    Console.WriteLine("PASS: retained canvas, sharp blocks, transparency, non-square source, unchanged original, cancellation.");
}
finally { Directory.Delete(root, true); }
