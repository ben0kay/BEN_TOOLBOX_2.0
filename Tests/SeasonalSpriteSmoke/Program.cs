using BEN_TOOLBOX_2_0.Models;
using BEN_TOOLBOX_2_0.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Cryptography;

static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
var root=Path.Combine(Path.GetTempPath(),"SeasonalSpriteSmoke_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
    string source=Path.Combine(root,"tree.png");
    using(var fixture=new Image<Rgba32>(40,64))
    {
        for(int y=5;y<35;y++)for(int x=5;x<35;x++)fixture[x,y]=new Rgba32(60,(byte)(100+y*3),35,255);
        for(int y=35;y<63;y++)for(int x=17;x<23;x++)fixture[x,y]=new Rgba32(125,75,35,255);
        fixture[5,5]=new Rgba32(60,180,35,128);fixture.SaveAsPng(source);
    }
    var hash=SHA256.HashData(File.ReadAllBytes(source));var token=CancellationToken.None;
    var p=SeasonalMaskService.Load(source,token);p.Automatic=SeasonalMaskService.Detect(p,p.Detection,token);
    int leaf=20*p.Width+20,bark=50*p.Width+20,empty=0;
    Check(p.Automatic[leaf]>180,"Green foliage not detected");Check(p.Automatic[bark]==0,"Bark selected");
    p.Protected[leaf]=255;Check(SeasonalMaskService.Effective(p,leaf)==0,"Protection must win");
    p.Protected[leaf]=0;p.Removed[leaf]=255;Check(SeasonalMaskService.Effective(p,leaf)==0,"Removal must win");
    p.Added[empty]=255;Check(SeasonalMaskService.Effective(p,empty)==0,"Transparent source selected");
    p.Removed[leaf]=0;
    var oldAdded=p.Added.ToArray();p.Automatic=SeasonalMaskService.Detect(p,p.Detection with {Tolerance=80},token);
    Check(oldAdded.SequenceEqual(p.Added),"Regeneration wiped manual layer");
    string mask=Path.Combine(root,"tree_foliage_mask.png");SeasonalMaskService.ExportMask(p,mask,token);
    using(var image=Image.Load<Rgba32>(mask))
    {
        Check(image.Width==40&&image.Height==64,"Mask resized");
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
        {var c=image[x,y];Check(c.R==c.G&&c.G==c.B&&c.A==255,"Incorrect mask PNG channel contract");}
        Check(image[0,0].R==0,"Transparent source mask is nonzero");
    }
    try{SeasonalMaskService.ExportMask(p,mask,token);throw new Exception("Mask overwrite allowed");}catch(IOException){}
    foreach(var season in Enum.GetValues<StudioSeason>())
    {
        p.Look=new(season);var result=SeasonalMaskService.Render(p,StudioView.Seasonal,0.5,true,true,token);
        using var image=Image.Load<Rgba32>(result);
        Check(image[20,50].Equals(new Rgba32(125,75,35,255)),"Season affected unselected bark");
        for(int y=0;y<64;y++)for(int x=0;x<40;x++)Check(image[x,y].A==p.Rgba[(y*40+x)*4+3],"Season changed alpha");
    }
    p.Look=new(StudioSeason.Autumn,0);var original=SeasonalMaskService.Render(p,StudioView.Original,0,true,true,token);
    Check(original.SequenceEqual(SeasonalMaskService.Render(p,StudioView.Seasonal,0,true,true,token)),"Zero effect changes source");
    string project=Path.Combine(root,"tree.seasonal.json");SeasonalProjectStore.Save(p,project,token);
    var opened=SeasonalProjectStore.Load(project,token);Check(!opened.SourceChanged,"Unchanged source flagged");
    Check(opened.Project.Automatic.SequenceEqual(p.Automatic)&&opened.Project.Added.SequenceEqual(p.Added)&&opened.Project.Protected.SequenceEqual(p.Protected),"Project masks changed");
    var history=new MaskHistory();p.Added[leaf]=80;history.Push(new(new[]{new MaskDelta(0,leaf,0,80)}));history.Undo(p);Check(p.Added[leaf]==0,"Undo failed");history.Redo(p);Check(p.Added[leaf]==80,"Redo failed");
    Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))),"Source changed");
    using(var changed=Image.Load<Rgba32>(source)){changed[20,20]=new Rgba32(255,0,0);changed.SaveAsPng(source);}
    Check(SeasonalProjectStore.Load(project,token).SourceChanged,"Changed-source hash not detected");
    using(var changed=new Image<Rgba32>(41,64))changed.SaveAsPng(source);
    try{SeasonalProjectStore.Load(project,token);throw new Exception("Wrong-sized source accepted");}catch(IOException){}
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();
    try{SeasonalMaskService.Detect(p,p.Detection,cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
    Console.WriteLine("PASS: foliage/bark, protection/removal, transparent gating, mask PNG contract, source safety, seasons, save/load, identity, undo/redo and cancellation.");
}
finally{Directory.Delete(root,true);}
