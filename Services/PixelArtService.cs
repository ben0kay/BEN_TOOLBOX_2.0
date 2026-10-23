using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record PixelArtOptions(int LogicalWidth = 128, bool NearestSource = false);

// A coarse logical grid, magnified sharply back to the source canvas. No palette quantization.
public static class PixelArtService
{
    public static byte[] Preview(string path, PixelArtOptions options, CancellationToken token)
    {
        if (options.LogicalWidth is < 16 or > 512) throw new ArgumentException("Detail must be 16–512.");
        token.ThrowIfCancellationRequested();
        var info = Image.Identify(path) ?? throw new IOException("Unreadable PNG.");
        if (info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 32000000)
            throw new IOException("Preview limit: 8192 per side and 32 million pixels.");
        using var image = Image.Load<Rgba32>(path);
        int width = image.Width, height = image.Height;
        int gridWidth = Math.Min(width, options.LogicalWidth);
        int gridHeight = Math.Clamp((int)Math.Round((double)height * gridWidth / width), 1, height);
        token.ThrowIfCancellationRequested();
        image.Mutate(x => x.Resize(gridWidth, gridHeight,
            options.NearestSource ? KnownResamplers.NearestNeighbor : KnownResamplers.Box));
        token.ThrowIfCancellationRequested();
        image.Mutate(x => x.Resize(width, height, KnownResamplers.NearestNeighbor));
        token.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
