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
    var outputFolder = Path.Combine(root,"output"); Directory.CreateDirectory(outputFolder);
    var exportOptions = new PixelArtExportOptions(new[]{path},outputFolder,new(64));
    var plan = await PixelArtExportService.PlanAsync(exportOptions,CancellationToken.None);
    if(plan.Length!=1 || Path.GetFileName(plan[0].Output)!="source_px64.png")throw new Exception("Naming plan incorrect.");
    await PixelArtExportService.ExportAsync(exportOptions,null,CancellationToken.None);
    using(var exported=Image.Load<Rgba32>(plan[0].Output))
        if(exported.Width!=1200 || exported.Height!=800 || exported[0,0].A!=0)throw new Exception("Export dimensions/alpha failure.");
    var existingOutput=File.ReadAllBytes(plan[0].Output);
    try { await PixelArtExportService.ExportAsync(exportOptions,null,CancellationToken.None);throw new Exception("Output overwrite allowed."); }
    catch(IOException){}
    if(!existingOutput.SequenceEqual(File.ReadAllBytes(plan[0].Output)))throw new Exception("Existing output modified.");
    var duplicateSource=Path.Combine(root,"other","source.png");Directory.CreateDirectory(Path.GetDirectoryName(duplicateSource)!);File.Copy(path,duplicateSource);
    try { await PixelArtExportService.PlanAsync(exportOptions with { Sources=new[]{path,duplicateSource},Destination=root },CancellationToken.None);throw new Exception("Duplicate targets accepted."); }
    catch(IOException){}
    var lease=await PixelArtHandoffLease.CreateAsync(new[]{path,duplicateSource},new(64),null,CancellationToken.None);
    string staged=lease.Files[0];
    try
    {
        if(lease.Files.Length!=2 || lease.Files.Distinct().Count()!=2 || !lease.Files.All(File.Exists))throw new Exception("Invalid staged handoff.");
        var batchFolder=Path.Combine(root,"batch");Directory.CreateDirectory(batchFolder);
        var batchOptions=new SpriteBatchOptions("",batchFolder,"Tree",0.5,1,3,false,false,false,lease.Files,true);
        await SpriteBatchService.ExportAsync(batchOptions,null,CancellationToken.None);
        using var resized=Image.Load<Rgba32>(Path.Combine(batchFolder,"Tree001.png"));
        if(resized.Width!=600 || resized.Height!=400)throw new Exception("Batch handoff resize failure.");
    }
    finally {lease.Dispose();}
    if(File.Exists(staged) || !File.Exists(path))throw new Exception("Handoff cleanup/source preservation failure.");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { PixelArtService.Preview(path, new(64), cancellation.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
    Console.WriteLine("PASS: retained canvas, sharp blocks, transparency, non-square source, unchanged original, cancellation, export, collisions and Batch handoff.");
}
finally { Directory.Delete(root, true); }
