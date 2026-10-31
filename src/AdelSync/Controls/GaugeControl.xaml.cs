using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;

namespace AdelSync.Controls;

public partial class GaugeControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeControl),
            new PropertyMetadata(0d, (_, __) => ((GaugeControl)_).Redraw()));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(GaugeControl),
            new PropertyMetadata("CPU экон.", (d, e) =>
            {
                ((GaugeControl)d).CaptionText.Text = e.NewValue?.ToString() ?? "";
            }));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(MediaBrush), typeof(GaugeControl),
            new PropertyMetadata(null, (d, e) =>
            {
                if (e.NewValue is MediaBrush b)
                    ((GaugeControl)d).Arc.Stroke = b;
            }));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public MediaBrush? Accent
    {
        get => (MediaBrush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public GaugeControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    private void Redraw()
    {
        var v = Math.Clamp(Value, 0, 100);
        ValueText.Text = ((int)Math.Round(v)).ToString();

        // Arc from 200° to -20° (almost full bottom-open gauge), sweep proportional to value.
        const double startDeg = 200;
        const double totalSweep = 220;
        var sweep = totalSweep * (v / 100.0);

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        var cx = w / 2;
        var cy = h / 2 + 8;
        var r = Math.Min(w, h) * 0.38;

        Arc.Data = BuildArc(cx, cy, r, startDeg, sweep);
    }

    private static Geometry BuildArc(double cx, double cy, double r, double startDeg, double sweepDeg)
    {
        if (sweepDeg <= 0.5)
            return Geometry.Empty;

        Point Polar(double deg)
        {
            var rad = deg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        var start = Polar(startDeg);
        var end = Polar(startDeg + sweepDeg);
        var large = sweepDeg > 180;

        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            IsLargeArc = large,
            SweepDirection = SweepDirection.Clockwise
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }
}
