namespace BEN_TOOLBOX_2_0.Models;

public sealed record BranchPoint(double X,double Y);
public sealed record BranchPrototype(BranchPoint? Root = null, BranchPoint[]? Tips = null,
    double Thickness = 12, double Curvature = 0.2, int Forks = 2, int Seed = 17,
    int LeafCutoff = 32, string Colour = "#73503A", double ColourVariation = 0.18, bool PreserveBrown = true);
