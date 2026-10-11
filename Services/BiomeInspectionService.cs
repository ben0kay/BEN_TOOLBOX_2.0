using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record BiomeOption(string Name,string Id,bool Enabled,string Path)
{
    public override string ToString()=>Name+(Enabled?"":" [DISABLED]");
}
public sealed record PopulationRow(string Group,string Species,string Id,double Weight,string Share,string Artwork,string Status,string File)
{
    public string Display=>$"{Group} / {Species}   •   Weight {Weight:0.##}   •   Share {Share}\n{Status} / {Artwork}\n{File}";
}
public sealed record BiomeInspection(string Summary,List<PopulationRow> Rows);
internal sealed class TresDoc
{
    public string File {get;init;}="";
    public Dictionary<string,string> Externals {get;}=new(StringComparer.Ordinal);
    public Dictionary<string,Dictionary<string,string>> Sections {get;}=new(StringComparer.Ordinal);
    public Dictionary<string,string> Root=>Sections.GetValueOrDefault("resource")??new();
    public string Header {get;set;}="";
}
internal sealed record TresRef(TresDoc Doc,Dictionary<string,string> Section,string Path);
public sealed class BiomeInspectionService
{
    private readonly string root;
    private readonly Dictionary<string,TresDoc> cache=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex RefPattern=new(@"(ExtResource|SubResource)\(""([^""]+)""\)");
    private static readonly Regex ArrayPattern=new(@"(?:^Array\[.*\]\(\[(.*)\]\)$|^\[(.*)\]$)");
    private static readonly Regex AttributeId=new(@"\bid=""([^""]+)""");
    private static readonly Regex AttributePath=new(@"\bpath=""([^""]+)""");
    private static readonly Regex ScriptClass=new(@"script_class=""([^""]+)""");
    public BiomeInspectionService(string projectRoot)
    {
        root=Path.GetFullPath(projectRoot);
        if(!File.Exists(Path.Combine(root,"project.godot")))throw new IOException("Select the folder containing project.godot.");
    }
    private string? ResolvePath(string res)
    {
        if(!res.StartsWith("res://",StringComparison.OrdinalIgnoreCase))return null;
        var full=Path.GetFullPath(Path.Combine(root,res[6..].Replace('/',Path.DirectorySeparatorChar)));
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?full:null;
    }
    private TresDoc? Read(string? path)
    {
        if(path==null||!File.Exists(path))return null;
        if(cache.TryGetValue(path,out var existing))return existing;
        var doc=new TresDoc {File=path};
        cache[path]=doc;
        Dictionary<string,string>? section=null;
        foreach(var line in File.ReadLines(path))
        {
            var s=line.Trim();
            if(s.StartsWith("[gd_resource")){doc.Header=s;continue;}
            if(s.StartsWith("[ext_resource"))
            {
                var id=AttributeId.Match(s);var p=AttributePath.Match(s);
                if(id.Success&&p.Success)doc.Externals[id.Groups[1].Value]=p.Groups[1].Value;
                section=null;continue;
            }
            if(s.StartsWith("[resource]"))section=doc.Sections["resource"]=new();
            else if(s.StartsWith("[sub_resource"))
            {
                var id=AttributeId.Match(s);
                section=id.Success?doc.Sections["sub:"+id.Groups[1].Value]=new():null;
            }
            else if(s.StartsWith("["))section=null;
            else if(section!=null)
            {
                var split=s.IndexOf('=');
                if(split>0)section[s[..split].Trim()]=s[(split+1)..].Trim();
            }
        }
        return doc;
    }
    private TresRef? Resolve(TresDoc doc,string expression)
    {
        var match=RefPattern.Match(expression);
        if(!match.Success)return null;
        var id=match.Groups[2].Value;
        if(match.Groups[1].Value=="SubResource")
            return doc.Sections.TryGetValue("sub:"+id,out var inline)?new(doc,inline,doc.File):null;
        if(!doc.Externals.TryGetValue(id,out var res))return null;
        var path=ResolvePath(res);if(path==null||!path.EndsWith(".tres",StringComparison.OrdinalIgnoreCase))return null;
        var other=Read(path);return other==null?null:new(other,other.Root,other.File);
    }
    private static string Val(Dictionary<string,string>? section,string key,string fallback="")=>
        section!=null&&section.TryGetValue(key,out var text)?text.Trim().Trim('"'):fallback;
    private static double Num(Dictionary<string,string>? section,string key,double fallback=0)=>
        double.TryParse(Val(section,key),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:fallback;
    private IEnumerable<TresRef?> Items(TresRef owner,string key)
    {
        var expression=Val(owner.Section,key);
        var m=ArrayPattern.Match(expression);if(!m.Success)yield break;
        var body=m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value;
        foreach(Match item in RefPattern.Matches(body))
            yield return Resolve(owner.Doc,item.Value);
    }
    private string Relative(string p)=>Path.GetRelativePath(root,p);
    public Task<List<BiomeOption>> GetBiomesAsync(CancellationToken ct)=>Task.Run(()=>GetBiomes(ct),ct);
    private List<BiomeOption> GetBiomes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        cache.Clear();
        var catalog=Read(Path.Combine(root,"WORLD","Generation","Biomes","BiomeCatalog.tres"))
            ??throw new FileNotFoundException("WORLD/Generation/Biomes/BiomeCatalog.tres not found.");
        var owner=new TresRef(catalog,catalog.Root,catalog.File);
        var list=new List<BiomeOption>();
        foreach(var biome in Items(owner,"Biomes"))
        {
            ct.ThrowIfCancellationRequested();
            if(biome==null)continue;
            list.Add(new(Val(biome.Section,"DisplayName",Path.GetFileNameWithoutExtension(biome.Path)),
                Val(biome.Section,"Id","(default)"),Val(biome.Section,"Enabled","true")!="false",biome.Path));
        }
        return list;
    }
    private string Artwork(TresRef? definition)
    {
        if(definition==null)return "Missing definition";
        var visual=Resolve(definition.Doc,Val(definition.Section,"Visual",Val(definition.Section,"VisualOverride")));
        if(visual==null)return "Procedural / fallback";
        var folder=Val(visual.Section,"ImageFolder");
        if(folder.Length>0)
        {
            var path=ResolvePath(folder);
            if(path==null||!Directory.Exists(path))return "ImageFolder MISSING";
            return Directory.EnumerateFiles(path,"*.png",SearchOption.TopDirectoryOnly).Count()+" PNG variant(s)";
        }
        if(visual.Section.ContainsKey("Image"))return "Single image";
        if(visual.Section.ContainsKey("VisualScene"))return "Visual scene";
        return "Procedural / fallback";
    }
    private void AddFamily(List<PopulationRow> rows,string family,TresRef? owner,bool enabled)
    {
        if(owner==null)return;
        var entries=Items(owner,family).Select(entry=>(
            entry,definition:entry==null?null:Resolve(entry.Doc,Val(entry.Section,"Definition")),
            weight:entry==null?0:Num(entry.Section,"Weight",1))).ToList();
        double total=entries.Where(e=>e.definition!=null&&e.weight>0).Sum(e=>e.weight);
        foreach(var e in entries)
        {
            var def=e.definition;
            var status=def==null?"Missing reference":!enabled?"Family disabled":e.weight<0?"Invalid weight":e.weight==0?"Weight zero":"Active";
            rows.Add(new(family,def==null?"(missing definition)":Path.GetFileNameWithoutExtension(def.Path),
                def==null?"":Val(def.Section,"Id"),e.weight,total>0&&e.weight>0?$"{100*e.weight/total:N1}%":"0%",
                Artwork(def),status,def==null?"(missing)":Relative(def.Path)));
        }
    }
    private void AddEnemies(List<PopulationRow> rows,TresRef content,bool enabled)
    {
        var population=Resolve(content.Doc,Val(content.Section,"Enemies"));if(population==null)return;
        var entries=Items(population,"Definitions").Select(def=>(def,weight:def==null?0:Num(def.Section,"SpawnWeight",1))).ToList();
        var total=entries.Where(e=>e.def!=null&&e.weight>0).Sum(e=>e.weight);
        foreach(var e in entries)
        {
            var def=e.def;
            rows.Add(new("Enemies",def==null?"(missing)":Val(def.Section,"DisplayName",Path.GetFileNameWithoutExtension(def.Path)),
                def==null?"":Val(def.Section,"SpeciesId"),e.weight,total>0&&e.weight>0?$"{100*e.weight/total:N1}%":"0%",
                Artwork(def),def==null?"Missing reference":!enabled?"Family disabled":e.weight<=0?"Inactive":"Active",
                def==null?"(missing)":Relative(def.Path)));
        }
    }
    public Task<BiomeInspection> InspectAsync(BiomeOption biome,CancellationToken ct)=>Task.Run(()=>Inspect(biome,ct),ct);
    private BiomeInspection Inspect(BiomeOption biome,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        cache.Clear();
        var doc=Read(biome.Path)??throw new FileNotFoundException("Biome resource not found",biome.Path);
        var biomeRef=new TresRef(doc,doc.Root,doc.File);
        var content=Resolve(doc,Val(doc.Root,"Content"))??biomeRef;
        var vegetation=Resolve(content.Doc,Val(content.Section,"Vegetation"));
        var rows=new List<PopulationRow>();
        var summary=new List<string> {$"BIOME: {biome.Name} [{biome.Id}] • {(biome.Enabled?"Enabled":"Disabled")}",
            "Population source: "+Relative(content.Path)};
        var counts=new Dictionary<string,double> {
            ["Plants"]=Num(vegetation?.Section,"PlantPatches",3)*Num(vegetation?.Section,"PlantsPerPatch",4),
            ["Trees"]=Num(vegetation?.Section,"TreesPerChunk",3),
            ["Grass"]=Num(vegetation?.Section,"GrassPatches",6)*Num(vegetation?.Section,"GrassTuftsPerPatch",10),
            ["Rocks"]=Num(content.Section,"RocksPerChunk",8)
        };
        var defaultDoc=Read(Path.Combine(root,"CONFIG","Biomes","BiomeDefaults.tres"));
        var defaults=defaultDoc==null?null:new TresRef(defaultDoc,defaultDoc.Root,defaultDoc.File);
        foreach(var family in new[]{"Plants","Trees","Grass","Rocks"})
        {
            ct.ThrowIfCancellationRequested();
            var owner=family=="Rocks"?content:vegetation;
            var placement=owner==null?null:Resolve(owner.Doc,Val(owner.Section,family+"Placement"));
            if(placement==null&&defaults!=null)placement=Resolve(defaults.Doc,Val(defaults.Section,family));
            var freq=Num(placement?.Section,"FrequencyMultiplier",1);
            var shape=Num(placement?.Section,"Distribution",0)==1?"Clumps":"Uniform";
            AddFamily(rows,family,owner,counts[family]>0&&freq>0);
            summary.Add($"{family}: {counts[family]:0.##} base x {freq:0.##} = {counts[family]*freq:0.##} target ({shape})");
        }
        var enemies=Resolve(content.Doc,Val(content.Section,"Enemies"));
        AddEnemies(rows,content,Num(enemies?.Section,"MaximumPerChunk",2)>0);
        summary.Add($"Enemies: {Num(enemies?.Section,"MinimumPerChunk"):0}–{Num(enemies?.Section,"MaximumPerChunk",2):0} per chunk");
        var vegFolder=Path.Combine(Path.GetDirectoryName(biome.Path)!,"Vegetation");
        var registered=rows.Where(r=>r.Group is "Trees" or "Plants" or "Grass").Select(r=>r.File)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int missing=0;
        if(Directory.Exists(vegFolder))
            foreach(var path in Directory.EnumerateFiles(vegFolder,"*.tres",SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var local=Read(path);
                if(local==null||!ScriptClass.IsMatch(local.Header))continue;
                var cls=ScriptClass.Match(local.Header).Groups[1].Value;
                if(cls is not ("TreeDefinition" or "PlantDefinition" or "GrassDefinition"))continue;
                if(registered.Contains(Relative(path)))continue;
                rows.Add(new("Unregistered",Path.GetFileNameWithoutExtension(path),Val(local.Root,"Id"),0,"-",
                    Artwork(new TresRef(local,local.Root,path)),"Not in active species list",Relative(path)));missing++;
            }
        summary.Add($"Unregistered local vegetation: {missing}");
        summary.Add("Target frequency is not guaranteed spawn count. This report reflects saved resources, not live runtime.");
        return new(string.Join(Environment.NewLine,summary),rows);
    }
}
