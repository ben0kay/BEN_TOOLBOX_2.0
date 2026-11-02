using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BEN_TOOLBOX_2_0.Shared.UI;

/// <summary>
/// Efficient, resize-aware sci-fi panel with diagonal cut corners and thin
/// illuminated HUD lines. Content is laid out by Decorator (including Padding);
/// only the background and border are custom-drawn, with no animation timer.
/// </summary>
public sealed class BtxTechFrame : Decorator
{
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<BtxTechFrame, IBrush?>(nameof(SurfaceBrush));

    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<BtxTechFrame, IBrush?>(nameof(OutlineBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<BtxTechFrame, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> GlowBrushProperty =
        AvaloniaProperty.Register<BtxTechFrame, IBrush?>(nameof(GlowBrush));

    public static readonly StyledProperty<bool> IsEmphasizedProperty =
        AvaloniaProperty.Register<BtxTechFrame, bool>(nameof(IsEmphasized));

    private StreamGeometry? _outline;
    private Size _outlineSize;

    static BtxTechFrame()
    {
        AffectsRender<BtxTechFrame>(
            SurfaceBrushProperty, OutlineBrushProperty, AccentBrushProperty,
            GlowBrushProperty, IsEmphasizedProperty);
    }

    public IBrush? SurfaceBrush
    {
        get => GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public IBrush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? GlowBrush
    {
        get => GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    public bool IsEmphasized
    {
        get => GetValue(IsEmphasizedProperty);
        set => SetValue(IsEmphasizedProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 3 || height < 3)
            return;

        if (_outline is null || _outlineSize != Bounds.Size)
        {
            _outline = CreateOutline(width, height);
            _outlineSize = Bounds.Size;
        }

        // Draw the wide translucent outline first for an inexpensive glow,
        // then the crisp 1px border and the dark gradient-filled surface.
        if (GlowBrush is not null)
            context.DrawGeometry(null, new Pen(GlowBrush, IsEmphasized ? 5 : 3), _outline);
        context.DrawGeometry(SurfaceBrush, new Pen(OutlineBrush ?? Brushes.Transparent, 1), _outline);

        if (AccentBrush is null || width < 54 || height < 32)
            return;

        var cut = Math.Min(12.0, Math.Min(width, height) * 0.22);
        var startX = cut + 10;
        var endX = Math.Max(startX + 2, Math.Min(width * 0.42, 230));
        endX = Math.Min(endX, width - cut - 4);

        // Two strokes on the upper edge create a bright, soft-lit flow.
        if (GlowBrush is not null)
            context.DrawLine(new Pen(GlowBrush, IsEmphasized ? 6 : 4),
                new Point(startX, 1.5), new Point(endX, 1.5));
        context.DrawLine(new Pen(AccentBrush, IsEmphasized ? 2 : 1.3),
            new Point(startX, 1.5), new Point(endX, 1.5));

        // A shorter bottom-right highlight balances the top-left light rail.
        var rightStart = Math.Max(cut + 4, width - Math.Min(width * 0.26, 125));
        context.DrawLine(new Pen(AccentBrush, IsEmphasized ? 1.7 : 1),
            new Point(rightStart, height - 1.5),
            new Point(width - cut - 8, height - 1.5));

        // Small bracket/tick along the left edge; no real-time redraw needed.
        context.DrawLine(new Pen(AccentBrush, 1),
            new Point(1.5, Math.Min(30, height * 0.35)),
            new Point(1.5, Math.Min(44, height * 0.55)));
    }

    private static StreamGeometry CreateOutline(double width, double height)
    {
        var cut = Math.Min(12.0, Math.Min(width, height) * 0.22);
        var x = width - 1;
        var y = height - 1;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            // Chamfer top-right and bottom-left, keeping edge geometry inside
            // the control so no external clipping or layout inflation is needed.
            path.BeginFigure(new Point(1, 1), true);
            path.LineTo(new Point(x - cut, 1));
            path.LineTo(new Point(x, 1 + cut));
            path.LineTo(new Point(x, y));
            path.LineTo(new Point(1 + cut, y));
            path.LineTo(new Point(1, y - cut));
            path.EndFigure(true);
        }

        return geometry;
    }
}
