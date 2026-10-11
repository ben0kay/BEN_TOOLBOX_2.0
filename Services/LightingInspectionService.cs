using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BEN_TOOLBOX_2_0.Services;

public sealed record LightingSetting(string Kind,string Setting,string Value,string Section,string File,int Line)
{
    public string Display => $"{Kind} / {Setting} = {Value}\n{Section} • {File}:{Line}";
}
public static class LightingInspectionService
{
    private static readonly string[] ConfigFiles = {
        "VISUALS/Lighting/WorldLightingSettings.cs",
        "VISUALS/Lighting/VisualLightingSettings.cs",
        "VISUALS/Shadows/GroundShadowSettings.cs"
    };
    private static readonly Regex Property=new(@"public\s+\w+\s+(\w+)\s*\{\s*get;\s*set;\s*\}\s*(?:=\s*(.+);)?");
    private static readonly Regex Group=new(@"\[ExportGroup\(""([^""]+)""\)\]");
    private static readonly Regex Uniform=new(@"^(?:global\s+)?uniform\s+\w+\s+(\w+)(.*);");
    private static readonly Regex Pair=new(@"^([^=]+?)\s*=\s*(.*)$");
    private static readonly Regex Ext=new(@"ExtResource\(""([^""]+)""\)");
    private static readonly Regex ExtId=new(@"\bid=""([^""]+)""");
    private static readonly Regex ExtPath=new(@"\bpath=""([^""]+)""");
    private static readonly Regex Name=new(@"(?i)(sun|shadow|ambient|lighting|^wl_)");
    private static readonly HashSet<string> Skip=new(StringComparer.OrdinalIgnoreCase)
    {".git",".godot",".vs","bin","obj","BEN_TOOLBOX","BEN_TOOLBOX_OLDVERSION"};
    public static Task<List<LightingSetting>> ReadAsync(string root,CancellationToken ct)=>Task.Run(()=>Read(root,ct),ct);
    private static List<LightingSetting> Read(string root,CancellationToken ct)
    {
        if(!File.Exists(Path.Combine(root,"project.godot")))throw new IOException("Choose a Godot project folder containing project.godot.");
        var output=new List<LightingSetting>();
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var relative in ConfigFiles)
        {
            ct.ThrowIfCancellationRequested();
            var file=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
            if(!File.Exists(file))continue;
            var lines=File.ReadAllLines(file);var group="Configuration";bool exported=false;
            for(int i=0;i<lines.Length;i++)
            {
                var line=lines[i].Trim();
                var g=Group.Match(line);
                if(g.Success){group=g.Groups[1].Value;exported=false;}
                if(line.Contains("[Export"))exported=true;
                var p=Property.Match(line);
                if(!exported||!p.Success)continue;
                var property=p.Groups[1].Value;
                names.Add(property);
                output.Add(new("C# default",property,p.Groups[2].Success?p.Groups[2].Value:"Implicit default",group,relative,i+1));
                exported=false;
            }
        }
        foreach(var file in Walk(root,ct))
        {
            var ext=Path.GetExtension(file).ToLowerInvariant();
            var relative=Path.GetRelativePath(root,file);
            if(ext is not (".tres" or ".tscn" or ".gdshader" or ".gdshaderinc") && Path.GetFileName(file)!="project.godot")continue;
            var lines=File.ReadAllLines(file);
            if(ext is ".gdshader" or ".gdshaderinc")
            {
                for(int i=0;i<lines.Length;i++)
                {
                    var m=Uniform.Match(lines[i].Trim());
                    if(!m.Success)continue;
                    var n=m.Groups[1].Value;
                    if(!relative.Contains("Light",StringComparison.OrdinalIgnoreCase)&&
                       !relative.Contains("Shadow",StringComparison.OrdinalIgnoreCase)&&
                       !relative.Contains("GroundSun",StringComparison.OrdinalIgnoreCase)&&!Name.IsMatch(n))continue;
                    var tail=m.Groups[2].Value;
                    output.Add(new(lines[i].TrimStart().StartsWith("global")?"Shader global declaration":"Shader default",n,
                        tail.Contains('=')?tail[(tail.IndexOf('=')+1)..].Trim():"No declaration default","Shader",relative,i+1));
                }
                continue;
            }
            var resources=new Dictionary<string,string>();
            foreach(var line in lines.Where(l=>l.TrimStart().StartsWith("[ext_resource")))
            {
                var id=ExtId.Match(line);var path=ExtPath.Match(line);
                if(id.Success&&path.Success)resources[id.Groups[1].Value]=path.Groups[1].Value;
            }
            string section="";var block=new List<(string Key,string Value,int Line)>();
            void Flush()
            {
                if(section.StartsWith("[ext_resource")){block.Clear();return;}
                var context=section+" "+string.Join(" ",block.Select(b=>b.Value+" "+string.Join(" ",Ext.Matches(b.Value).Cast<Match>()
                    .Select(m=>resources.GetValueOrDefault(m.Groups[1].Value,"")))));
                var relevant=context.Contains("Lighting",StringComparison.OrdinalIgnoreCase) ||
                    context.Contains("Shadow",StringComparison.OrdinalIgnoreCase) ||
                    context.Contains("GroundSun",StringComparison.OrdinalIgnoreCase) ||
                    ((relative.Contains("Lighting",StringComparison.OrdinalIgnoreCase) || relative.Contains("Shadow",StringComparison.OrdinalIgnoreCase)) && section=="[resource]");
                foreach(var (key,value,line) in block)
                {
                    if(!Name.IsMatch(key) && !(relevant&&(names.Contains(key)||key=="Profile"||key=="script"||key.StartsWith("shader_parameter/"))))continue;
                    var id=Ext.Match(value);
                    var resolved=id.Success&&resources.TryGetValue(id.Groups[1].Value,out var path) ? value+" -> "+path:value;
                    output.Add(new(Path.GetFileName(file)=="project.godot"?"Project shader global":ext==".tscn"?"Scene saved value":"Resource saved value",
                        key,resolved,section,relative,line));
                }
                block.Clear();
            }
            for(int i=0;i<lines.Length;i++)
            {
                var line=lines[i].Trim();
                if(line.StartsWith("["))
                {
                    Flush();section=line;
                }
                else
                {
                    var m=Pair.Match(line);
                    if(m.Success)block.Add((m.Groups[1].Value.Trim(),m.Groups[2].Value.Trim(),i+1));
                }
            }
            Flush();
        }
        return output;
    }
    private static IEnumerable<string> Walk(string root,CancellationToken ct)
    {
        var stack=new Stack<string>();stack.Push(root);
        while(stack.Count>0)
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(stack.Pop()))
            {
                ct.ThrowIfCancellationRequested();
                var attr=File.GetAttributes(path);
                if((attr&FileAttributes.ReparsePoint)!=0)continue;
                if((attr&FileAttributes.Directory)!=0)
                {if(!Skip.Contains(Path.GetFileName(path)))stack.Push(path);}
                else yield return path;
            }
        }
    }
}
