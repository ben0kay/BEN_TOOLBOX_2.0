using System.Buffers.Binary;
using BEN_TOOLBOX_2_0.Services;

var root = Path.Combine(Path.GetTempPath(), "SpriteInventorySmoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5");
    byte[] png = new byte[32];
    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
    "IHDR"u8.CopyTo(png.AsSpan(12, 4));
    BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 1024);
    BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 768);
    Test("tree.png", png, 1024, 768);

    byte[] gif = new byte[10];
    "GIF89a"u8.CopyTo(gif);
    BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6, 2), 320);
    BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8, 2), 200);
    Test("plant.gif", gif, 320, 200);

    byte[] bmp = new byte[26]; bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
    BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14, 4), 40);
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18, 4), 400);
    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22, 4), -250);
    Test("rock.bmp", bmp, 400, 250);

    byte[] vp8x = Webp("VP8X"); vp8x[24] = 127; vp8x[25] = 2; vp8x[27] = 223; vp8x[28] = 1;
    Test("moss.webp", vp8x, 640, 480);
    byte[] vp8l = Webp("VP8L"); vp8l[20] = 0x2f;
    vp8l[21] = 63; vp8l[22] = 0xc0; vp8l[23] = 31;
    Test("leaf.webp", vp8l, 64, 128);
    byte[] vp8 = Webp("VP8 "); vp8[23] = 0x9d; vp8[24] = 1; vp8[25] = 0x2a;
    vp8[26] = 64; vp8[27] = 1; vp8[28] = 240;
    Test("ground.webp", vp8, 320, 240);

    byte[] jpeg =
    {
        0xff, 0xd8, 0xff, 0xc0, 0, 17, 8,
        0, 120, 0, 200, 3, 1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0
    };
    Test("creature.jpg", jpeg, 200, 120);

    File.WriteAllBytes(Path.Combine(root, "corrupt.png"), new byte[] { 1, 2, 3 });
    File.WriteAllBytes(Path.Combine(root, "wrong_extension.png"), gif);
    Directory.CreateDirectory(Path.Combine(root, "bin"));
    File.WriteAllBytes(Path.Combine(root, "bin", "ignore.png"), png);
    Directory.CreateDirectory(Path.Combine(root, ".godot"));
    File.WriteAllBytes(Path.Combine(root, ".godot", "ignore.png"), png);

    var report = SpriteInventoryService.Scan(root, CancellationToken.None);
    Check(report.Sprites.Count == 7, "Expected 7 sprites; got " + report.Sprites.Count);
    Check(report.Issues.Count == 2, "Expected 2 errors; got " + report.Issues.Count);
    Check(report.Sprites.All(x => !x.RelativePath.Contains("ignore")), "Generated folders not ignored");
    Check(report.TotalBytes > 0, "Expected nonzero byte count");

    using var source = new CancellationTokenSource();
    source.Cancel();
    try { SpriteInventoryService.Scan(root, source.Token); throw new Exception("Cancel failed"); }
    catch (OperationCanceledException) { }
    Console.WriteLine("PASS: PNG, WebP VP8/VP8L/VP8X, JPEG, GIF, BMP, corrupt files, excluded folders, cancellation.");
}
finally { Directory.Delete(root, recursive: true); }

void Test(string name, byte[] bytes, int w, int h)
{
    var path = Path.Combine(root, name);
    File.WriteAllBytes(path, bytes);
    var found = SpriteInventoryService.ReadDimensions(path);
    Check(found.Width == w && found.Height == h, name + ": unexpected dimensions " + found);
}

static byte[] Webp(string kind)
{
    var b = new byte[30];
    "RIFF"u8.CopyTo(b);
    "WEBP"u8.CopyTo(b.AsSpan(8));
    System.Text.Encoding.ASCII.GetBytes(kind).CopyTo(b, 12);
    return b;
}

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
