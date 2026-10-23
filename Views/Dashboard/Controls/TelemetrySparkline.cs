using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views.Dashboard.Controls;

/// <summary>Reusable 60-sample chart; no timers, animations, or child visuals.</summary>
public sealed class TelemetrySparkline : Control
{
    private const double Inset = 4;
    private readonly TelemetryHistory _history = new();

    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(48, 90, 144, 173)), 1);
    private static readonly Pen CyanLine = new(new SolidColorBrush(Color.FromRgb(97, 215, 242)), 1.8);
    private static readonly Pen CyanGlow = new(new SolidColorBrush(Color.FromArgb(60, 97, 215, 242)), 4.5);
    private static readonly Pen AmberLine = new(new SolidColorBrush(Color.FromRgb(236, 177, 91)), 1.8);
    private static readonly Pen AmberGlow = new(new SolidColorBrush(Color.FromArgb(60, 236, 177, 91)), 4.5);
    private static readonly IBrush CyanDot = new SolidColorBrush(Color.FromRgb(130, 233, 250));
    private static readonly IBrush AmberDot = new SolidColorBrush(Color.FromRgb(250, 207, 127));

    public static readonly StyledProperty<bool> UseAmberProperty =
        AvaloniaProperty.Register<TelemetrySparkline, bool>(nameof(UseAmber));

    public bool UseAmber
    {
        get => GetValue(UseAmberProperty);
        set => SetValue(UseAmberProperty, value);
    }

    public int SampleCount => _history.Count;
    public int ValidSampleCount => _history.ValidCount;

    public void AddSample(double? percentage)
    {
        _history.Add(percentage);
        InvalidateVisual();
    }

    public void ClearSamples()
    {
        if (_history.Count == 0) return;
        _history.Clear();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= Inset * 2 || height <= Inset * 2) return;

        double left = Inset;
        double right = width - Inset;
        double bottom = height - Inset;
        double plotWidth = right - left;
        double plotHeight = height - Inset * 2;

        for (int level = 25; level <= 75; level += 25)
        {
            double y = bottom - plotHeight * level / 100d;
            context.DrawLine(GridPen, new Point(left, y), new Point(right, y));
        }

        Pen line = UseAmber ? AmberLine : CyanLine;
        Pen glow = UseAmber ? AmberGlow : CyanGlow;
        IBrush dot = UseAmber ? AmberDot : CyanDot;
        Point? previous = null;
        Point? latest = null;

        // The first samples collect from the right; when full, history scrolls left.
        for (int i = 0; i < _history.Count; i++)
        {
            double? value = _history.GetOldestFirst(i);
            if (!value.HasValue)
            {
                previous = null; // Missing readings must not become fake zeroes.
                continue;
            }

            int slot = _history.Capacity - _history.Count + i;
            double x = left + plotWidth * slot / (_history.Capacity - 1);
            double y = bottom - plotHeight * value.Value / 100d;
            var current = new Point(x, y);
            if (previous is Point prior)
            {
                context.DrawLine(glow, prior, current);
                context.DrawLine(line, prior, current);
            }
            previous = current;
            latest = current;
        }

        if (latest is Point point)
            context.DrawEllipse(dot, null, point, 2.2, 2.2);
    }
}
