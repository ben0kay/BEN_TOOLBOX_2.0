using System;
using System.Collections.Generic;
using System.Linq;

namespace BEN_TOOLBOX_2_0.Models;

public sealed record FoliageDetection(double Hue = 110, double Tolerance = 55, double MinSaturation = 0.18,
    double MinLight = 0.03, int MinAlpha = 8, double CanopyBias = 0, double Softness = 0.2, int Grow = 0, int RemoveIslands = 0,
    string? PickColour = null, double ColourTolerance = 0.3);
public enum StudioSeason { Spring, Summer, Autumn, Winter }
public enum StudioView { Original, Overlay, AutomaticMask, ProtectionMask, EffectiveMask, Seasonal, Leafless, Branches }
public enum MaskBrush { Pan, Add, Remove, Protect, Unprotect, PickColour, BranchRoot, BranchTip }
public sealed record SeasonalLook(StudioSeason Season = StudioSeason.Summer, double Strength = 1,
    double AutumnProgress = 0.65, double Snow = 0.4, int SnowPatch = 12, int Seed = 17);

public sealed class SeasonalMaskProject
{
    public const int MaxPixels = 4_000_000;
    public string SourcePath { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Rgba { get; init; } = Array.Empty<byte>();
    public byte[] Automatic { get; set; } = Array.Empty<byte>();
    public byte[] Added { get; set; } = Array.Empty<byte>();
    public byte[] Removed { get; set; } = Array.Empty<byte>();
    public byte[] Protected { get; set; } = Array.Empty<byte>();
    public FoliageDetection Detection { get; set; } = new();
    public SeasonalLook Look { get; set; } = new();
    public BranchPrototype Branches { get; set; } = new();
    public SeasonalMaskProject Snapshot() => new()
    {
        SourcePath=SourcePath,SourceHash=SourceHash,Width=Width,Height=Height,Rgba=Rgba,
        Automatic=(byte[])Automatic.Clone(),Added=(byte[])Added.Clone(),Removed=(byte[])Removed.Clone(),Protected=(byte[])Protected.Clone(),Detection=Detection,Look=Look,Branches=Branches with { Tips=Branches.Tips?.ToArray() }
    };
}
public readonly record struct MaskDelta(int Layer, int Index, byte Before, byte After);
public sealed record MaskStroke(MaskDelta[] Changes);

// Undo stores bounded stroke deltas, not copies of source images or whole masks.
public sealed class MaskHistory
{
    private readonly List<MaskStroke> undo = new();
    private readonly Stack<MaskStroke> redo = new();
    private int entries;
    public void Clear(){undo.Clear();redo.Clear();entries=0;}
    public void Push(MaskStroke stroke)
    {
        if(stroke.Changes.Length==0)return;
        redo.Clear();undo.Add(stroke);entries+=stroke.Changes.Length;
        while(entries>1_000_000 || undo.Count>80){entries-=undo[0].Changes.Length;undo.RemoveAt(0);}
    }
    public bool Undo(SeasonalMaskProject p)
    {
        if(undo.Count==0)return false;
        var stroke=undo[^1];undo.RemoveAt(undo.Count-1);entries-=stroke.Changes.Length;
        foreach(var d in stroke.Changes)Layer(p,d.Layer)[d.Index]=d.Before;
        redo.Push(stroke);return true;
    }
    public bool Redo(SeasonalMaskProject p)
    {
        if(redo.Count==0)return false;
        var stroke=redo.Pop();foreach(var d in stroke.Changes)Layer(p,d.Layer)[d.Index]=d.After;
        undo.Add(stroke);entries+=stroke.Changes.Length;return true;
    }
    public static byte[] Layer(SeasonalMaskProject p,int layer)=>layer switch { 0=>p.Added,1=>p.Removed,_=>p.Protected };
}
