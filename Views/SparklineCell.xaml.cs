using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MySQLManager.Views;

public partial class SparklineCell : UserControl
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>),
            typeof(SparklineCell),
            new PropertyMetadata(null, (d, _) => ((SparklineCell)d).Draw()));

    public static readonly DependencyProperty CurrentValueProperty =
        DependencyProperty.Register(nameof(CurrentValue), typeof(double),
            typeof(SparklineCell));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double CurrentValue
    {
        get => (double)GetValue(CurrentValueProperty);
        set => SetValue(CurrentValueProperty, value);
    }

    public SparklineCell() => InitializeComponent();

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Draw();
    }

    private void Draw()
    {
        SparkCanvas.Children.Clear();
        var vals = Values?.ToList();
        if (vals == null || vals.Count < 2) return;

        double w = SparkCanvas.ActualWidth;
        double h = SparkCanvas.ActualHeight;
        if (w < 10 || h < 4) return;

        double min = vals.Min();
        double max = vals.Max();
        double range = max - min;
        if (range == 0) range = 1;

        var pad = 2.0;
        var pts = new PointCollection();
        for (int i = 0; i < vals.Count; i++)
        {
            double x = pad + (w - 2 * pad) * i / (vals.Count - 1);
            double y = h - pad - (h - 2 * pad) * (vals[i] - min) / range;
            pts.Add(new Point(x, y));
        }

        // Draw polyline
        var line = new Polyline
        {
            Points          = pts,
            Stroke          = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 1.2,
            StrokeLineJoin  = PenLineJoin.Round,
        };
        SparkCanvas.Children.Add(line);

        // Highlight last point
        var last = pts[pts.Count - 1];
        var dot = new Ellipse
        {
            Width  = 4, Height = 4,
            Fill   = (Brush)FindResource("AccentBrush"),
        };
        Canvas.SetLeft(dot, last.X - 2);
        Canvas.SetTop(dot,  last.Y - 2);
        SparkCanvas.Children.Add(dot);

        ValueLabel.Text = FormatValue(vals[vals.Count - 1]);
    }

    private static string FormatValue(double v)
    {
        if (Math.Abs(v) >= 1_000_000) return $"{v / 1_000_000:F1}M";
        if (Math.Abs(v) >= 1_000)     return $"{v / 1_000:F1}K";
        return v == Math.Floor(v) ? v.ToString("F0") : v.ToString("F2");
    }
}
