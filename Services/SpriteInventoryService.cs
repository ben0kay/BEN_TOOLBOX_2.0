using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

// Read-only metadata inventory. Never decodes full images during a scan.
public sealed record SpriteInventoryEntry(
    string FullPath, string RelativePath, string Format, int Width, int Height, long Bytes)
{
    public string Name => Path.GetFileName(FullPath);
    public long PixelCount => (long)Width * Height;
    public string Dimensions => $"{Width:N0} × {Height:N0}";
    public string FileSize => Bytes >= 1024 * 1024
        ? $"{Bytes / (1024.0 * 1024.0):N2} MiB"
        : $"{Bytes / 1024.0:N1} KiB";
}

public sealed record SpriteInventoryIssue(string RelativePath, string Reason);

public sealed record SpriteInventoryResult(
    IReadOnlyList<SpriteInventoryEntry> Sprites, IReadOnlyList<SpriteInventoryIssue> Issues)
{
    public long TotalBytes => Sprites.Sum(s => s.Bytes);
}

public static class SpriteInventoryService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".webp", ".jpg", ".jpeg", ".gif", ".bmp" };

    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".godot", ".vs", ".idea", "bin", "obj", "node_modules" };

    public static Task<SpriteInventoryResult> ScanAsync(string folder, CancellationToken cancel) =>
        Task.Run(() => Scan(folder, cancel), cancel);

    public static SpriteInventoryResult Scan(string folder, CancellationToken cancel)
    {
        if (!File.Exists(Path.Combine(folder, "project.godot")))
            throw new IOException("Select the Godot project root (folder containing project.godot).");

        var root = Path.GetFullPath(folder);
        var sprites = new List<SpriteInventoryEntry>();
        var issues = new List<SpriteInventoryIssue>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var item in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancel.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(root, item);
                    try
                    {
                        // Don't walk directory junctions/symlinks into other projects.
                        var attributes = File.GetAttributes(item);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (!IgnoredFolders.Contains(Path.GetFileName(item)))
                                pending.Push(item);
                            continue;
                        }
                        if (!Extensions.Contains(Path.GetExtension(item))) continue;
                        var bytes = new FileInfo(item).Length;
                        try
                        {
                            var (width, height) = ReadDimensions(item);
                            sprites.Add(new SpriteInventoryEntry(item, relative,
                                Path.GetExtension(item).TrimStart('.').ToUpperInvariant(),
                                width, height, bytes));
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
                        {
                            issues.Add(new SpriteInventoryIssue(relative, "Cannot identify image: " + e.Message));
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        issues.Add(new SpriteInventoryIssue(relative, "Cannot access file: " + e.Message));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                issues.Add(new SpriteInventoryIssue(Path.GetRelativePath(root, directory),
                    "Cannot enumerate directory: " + e.Message));
            }
        }
        return new SpriteInventoryResult(sprites, issues);
    }

    // Supported file formats are identified from their headers, not assumed from extensions.
    // Only a few bytes are read for PNG/GIF/BMP/WebP. JPEG markers are inspected up to 1 MiB.
    public static (int Width, int Height) ReadDimensions(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var dimensions = ext switch
        {
            ".png" => Png(stream),
            ".webp" => Webp(stream),
            ".jpg" or ".jpeg" => Jpeg(stream),
            ".gif" => Gif(stream),
            ".bmp" => Bmp(stream),
            _ => throw new ArgumentException("Unsupported image format.")
        };
        if (dimensions.Width is <= 0 or > 100_000 || dimensions.Height is <= 0 or > 100_000)
            throw new InvalidDataException("Invalid image dimensions.");
        return dimensions;
    }

    private static byte[] Header(Stream stream, int length)
    {
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static (int Width, int Height) Png(Stream stream)
    {
        var b = Header(stream, 24);
        if (!b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !b.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Not a valid PNG header.");
        return (checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16, 4))),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20, 4))));
    }

    private static (int Width, int Height) Gif(Stream stream)
    {
        var b = Header(stream, 10);
        if (!b.AsSpan(0, 6).SequenceEqual("GIF87a"u8)
            && !b.AsSpan(0, 6).SequenceEqual("GIF89a"u8))
            throw new InvalidDataException("Not a valid GIF header.");
        return (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(8, 2)));
    }

    private static (int Width, int Height) Bmp(Stream stream)
    {
        var b = Header(stream, 26);
        if (b[0] != 'B' || b[1] != 'M')
            throw new InvalidDataException("Not a valid BMP header.");
        var dibSize = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(14, 4));
        if (dibSize == 12)
            return (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(18, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(20, 2)));
        if (dibSize < 40)
            throw new InvalidDataException("Unsupported BMP header type.");
        var width = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18, 4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(22, 4));
        if (height == int.MinValue)
            throw new InvalidDataException("Invalid BMP height.");
        return (width, Math.Abs(height));
    }

    private static (int Width, int Height) Webp(Stream stream)
    {
        var b = Header(stream, 30);
        if (!b.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !b.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Not a valid WebP RIFF header.");

        var fourCc = b.AsSpan(12, 4);
        if (fourCc.SequenceEqual("VP8X"u8))
            return (1 + b[24] + (b[25] << 8) + (b[26] << 16),
                    1 + b[27] + (b[28] << 8) + (b[29] << 16));
        if (fourCc.SequenceEqual("VP8L"u8) && b[20] == 0x2f)
            return (1 + b[21] + ((b[22] & 0x3f) << 8),
                    1 + (b[22] >> 6) + (b[23] << 2) + ((b[24] & 0x0f) << 10));
        if (fourCc.SequenceEqual("VP8 "u8)
            && b[23] == 0x9d && b[24] == 0x01 && b[25] == 0x2a)
            return ((b[26] | (b[27] << 8)) & 0x3fff,
                    (b[28] | (b[29] << 8)) & 0x3fff);

        throw new InvalidDataException("Unsupported or malformed WebP bitstream header.");
    }

    private static (int Width, int Height) Jpeg(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadByte() != 0xff || r.ReadByte() != 0xd8)
            throw new InvalidDataException("Not a valid JPEG header.");

        var limit = Math.Min(stream.Length, 1024 * 1024);
        while (stream.Position < limit)
        {
            if (r.ReadByte() != 0xff) continue;
            byte marker;
            do { marker = r.ReadByte(); } while (marker == 0xff && stream.Position < limit);
            if (marker is 0x00 or 0xd8 or 0x01 or 0xd9
                || (marker >= 0xd0 && marker <= 0xd7)) continue;
            if (marker == 0xda) break; // Start of compressed image data.
            int length = (r.ReadByte() << 8) | r.ReadByte();
            if (length < 2 || stream.Position + length - 2 > stream.Length)
                throw new InvalidDataException("Malformed JPEG segment.");
            bool isSof = (marker >= 0xc0 && marker <= 0xc3)
                || (marker >= 0xc5 && marker <= 0xc7)
                || (marker >= 0xc9 && marker <= 0xcb)
                || (marker >= 0xcd && marker <= 0xcf);
            if (isSof)
            {
                if (length < 7) throw new InvalidDataException("Short JPEG frame header.");
                r.ReadByte(); // precision
                int h = (r.ReadByte() << 8) | r.ReadByte();
                int w = (r.ReadByte() << 8) | r.ReadByte();
                return (w, h);
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        throw new InvalidDataException("JPEG frame dimensions not found in first MiB.");
    }
}
