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
    foreach (var mode in new[] { PixelPalette.Auto16, PixelPalette.Auto32, PixelPalette.Auto64, PixelPalette.Endesga32, PixelPalette.Grayscale16 })
    {
        var options = new PixelArtOptions(64, true, mode, true, 1.1, 1.2, 0.9, 0.5);
        var bytes = PixelArtService.Preview(path, options, CancellationToken.None, logicalPreview: true);
        if (!bytes.SequenceEqual(PixelArtService.Preview(path, options, CancellationToken.None, logicalPreview: true)))
            throw new Exception("Palette pipeline is not deterministic.");
        using var reduced = Image.Load<Rgba32>(bytes);
        if (reduced.Width != 64 || reduced.Height != 64) throw new Exception("Logical preview dimensions incorrect.");
        var colours = new HashSet<Rgba32>();
        for (int y = 0; y < reduced.Height; y++) for (int x = 0; x < reduced.Width; x++) colours.Add(reduced[x,y]);
        int limit = mode is PixelPalette.Auto16 or PixelPalette.Grayscale16 ? 16 : mode == PixelPalette.Auto64 ? 64 : 32;
        if (colours.Count > limit) throw new Exception("Palette colour limit exceeded.");
    }
    var customOptions = new PixelArtOptions(64, true, PixelPalette.Custom, CustomColours: new[] { "#123456", "#ABCDEF" });
    using (var custom = Image.Load<Rgba32>(PixelArtService.Preview(path, customOptions, CancellationToken.None, logicalPreview: true)))
        for (int y=0;y<custom.Height;y++) for (int x=0;x<custom.Width;x++)
        {
            var pixel = custom[x,y];
            if (pixel.A != 255 || !(pixel.Equals(new Rgba32(0x12,0x34,0x56)) || pixel.Equals(new Rgba32(0xAB,0xCD,0xEF))))
                throw new Exception("Custom palette/alpha mismatch.");
        }
    if (PixelArtService.EndesgaColours().Length != 32) throw new Exception("Built-in palette changed.");
    try { PixelPalettePresets.Validate(new[] { "not hex" }); throw new Exception("Invalid palette accepted."); }
    catch (ArgumentException) { }
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
