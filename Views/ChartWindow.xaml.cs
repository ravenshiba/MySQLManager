using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MySQLManager.Views;

public partial class ChartWindow : Window
{
    private DataTable? _data;

    // Navicat-inspired palette
    private static readonly Brush[] Palette =
    {
        new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2)),
        new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E)),
        new SolidColorBrush(Color.FromRgb(0xF9, 0xAB, 0x00)),
        new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25)),
        new SolidColorBrush(Color.FromRgb(0x7C, 0x4D, 0xFF)),
        new SolidColorBrush(Color.FromRgb(0x00, 0x97, 0xA7)),
        new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
        new SolidColorBrush(Color.FromRgb(0x37, 0x74, 0x60)),
    };

    public ChartWindow(DataTable data)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        _data = data;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_data == null || _data.Columns.Count == 0) return;

        // Populate column combos
        var cols = _data.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        XAxisCombo.ItemsSource = cols;
        YAxisCombo.ItemsSource = cols;

        // Smart defaults: X = first string col, Y = first numeric col
        var strCol = cols.FirstOrDefault(c => !IsNumericColumn(_data, c)) ?? cols[0];
        var numCol = cols.FirstOrDefault(c => IsNumericColumn(_data, c))  ?? cols[Math.Min(1, cols.Count - 1)];
        XAxisCombo.SelectedItem = strCol;
        YAxisCombo.SelectedItem = numCol;

        DrawChart();
    }

    private void Options_Changed(object sender, SelectionChangedEventArgs e) => DrawChart();
    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)    => DrawChart();

    private void DrawChart()
    {
        if (_data == null) return;
        var xCol = XAxisCombo.SelectedItem?.ToString();
        var yCol = YAxisCombo.SelectedItem?.ToString();
        var type = (ChartTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "長條圖";
        var limit = int.Parse((LimitCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "20");

        if (xCol == null || yCol == null) return;

        ChartCanvas.Children.Clear();

        // Extract data
        var rows = _data.Rows.Cast<DataRow>()
                        .Take(limit)
                        .Select(r => (
                            X: r[xCol]?.ToString() ?? "",
                            Y: TryParseDouble(r[yCol]?.ToString()) ?? 0.0))
                        .ToList();

        if (!rows.Any()) return;

        double w = Math.Max(ChartCanvas.ActualWidth,  ChartScroll.ActualWidth  - 20);
        double h = Math.Max(ChartCanvas.ActualHeight, ChartScroll.ActualHeight - 20);
        ChartCanvas.Width  = Math.Max(w, rows.Count * 40 + 100);
        ChartCanvas.Height = h;

        switch (type)
        {
            case "長條圖": DrawBar(rows, w, h);  break;
            case "折線圖": DrawLine(rows, w, h); break;
            case "圓餅圖": DrawPie(rows, w, h);  break;
        }
    }

    // ── Bar Chart ─────────────────────────────────────────────

    private void DrawBar(List<(string X, double Y)> rows, double w, double h)
    {
        const double padL = 60, padR = 20, padT = 30, padB = 60;
        double chartW = w - padL - padR;
        double chartH = h - padT - padB;
        double maxVal = rows.Max(r => r.Y);
        if (maxVal == 0) maxVal = 1;

        // Grid lines + Y axis labels
        for (int i = 0; i <= 5; i++)
        {
            double y = padT + chartH * (1 - i / 5.0);
            DrawLine2(padL, y, padL + chartW, y,
                      new SolidColorBrush(Color.FromRgb(0xDA, 0xDC, 0xE0)), 0.7);
            var lbl = new TextBlock
            {
                Text = FormatNumber(maxVal * i / 5),
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6))
            };
            Canvas.SetLeft(lbl, padL - 48);
            Canvas.SetTop(lbl, y - 8);
            ChartCanvas.Children.Add(lbl);
        }

        // Bars
        double barW = Math.Min(chartW / rows.Count * 0.7, 60);
        double gap   = chartW / rows.Count;

        for (int i = 0; i < rows.Count; i++)
        {
            double barH  = chartH * (rows[i].Y / maxVal);
            double x     = padL + gap * i + (gap - barW) / 2;
            double y     = padT + chartH - barH;

            var rect = new Rectangle
            {
                Width  = barW, Height = Math.Max(barH, 1),
                Fill   = Palette[i % Palette.Length],
                RadiusX = 3, RadiusY = 3
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            ChartCanvas.Children.Add(rect);

            // Value label on top
            var valLbl = new TextBlock
            {
                Text = FormatNumber(rows[i].Y),
                FontSize = 10, Foreground = Palette[i % Palette.Length],
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(valLbl, x + barW / 2 - 12);
            Canvas.SetTop(valLbl, y - 16);
            ChartCanvas.Children.Add(valLbl);

            // X label
            var xLbl = new TextBlock
            {
                Text = rows[i].X.Length > 12 ? rows[i].X[..12] + "…" : rows[i].X,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68)),
                MaxWidth = barW + 20, TextWrapping = TextWrapping.Wrap
            };
            Canvas.SetLeft(xLbl, x - 5);
            Canvas.SetTop(xLbl,  padT + chartH + 6);
            ChartCanvas.Children.Add(xLbl);
        }

        // Axes
        DrawLine2(padL, padT, padL, padT + chartH, Brushes.Gray, 1);
        DrawLine2(padL, padT + chartH, padL + chartW, padT + chartH, Brushes.Gray, 1);
    }

    // ── Line Chart ────────────────────────────────────────────

    private void DrawLine(List<(string X, double Y)> rows, double w, double h)
    {
        const double padL = 60, padR = 20, padT = 30, padB = 60;
        double chartW = w - padL - padR;
        double chartH = h - padT - padB;
        double maxVal = rows.Max(r => r.Y);
        if (maxVal == 0) maxVal = 1;

        // Grid
        for (int i = 0; i <= 5; i++)
        {
            double y = padT + chartH * (1 - i / 5.0);
            DrawLine2(padL, y, padL + chartW, y,
                      new SolidColorBrush(Color.FromRgb(0xDA, 0xDC, 0xE0)), 0.7);
            var lbl = new TextBlock
            {
                Text = FormatNumber(maxVal * i / 5),
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6))
            };
            Canvas.SetLeft(lbl, padL - 48);
            Canvas.SetTop(lbl, y - 8);
            ChartCanvas.Children.Add(lbl);
        }

        // Line + points
        var points = rows.Select((r, i) => new Point(
            padL + chartW * i / Math.Max(rows.Count - 1, 1),
            padT + chartH * (1 - r.Y / maxVal))).ToList();

        // Area fill
        var pg = new PathGeometry();
        var pf = new PathFigure { StartPoint = new Point(points[0].X, padT + chartH) };
        pf.Segments.Add(new LineSegment(points[0], false));
        foreach (var p in points.Skip(1)) pf.Segments.Add(new LineSegment(p, true));
        pf.Segments.Add(new LineSegment(new Point(points[^1].X, padT + chartH), false));
        pf.IsClosed = true;
        pg.Figures.Add(pf);
        var areaFill = new LinearGradientBrush(
            Color.FromArgb(0x50, 0x19, 0x76, 0xD2),
            Color.FromArgb(0x08, 0x19, 0x76, 0xD2), 90);
        ChartCanvas.Children.Add(new System.Windows.Shapes.Path { Data = pg, Fill = areaFill });

        // Polyline
        var poly = new Polyline
        {
            Points          = new PointCollection(points),
            Stroke          = Palette[0],
            StrokeThickness = 2.5,
            StrokeLineJoin  = PenLineJoin.Round
        };
        ChartCanvas.Children.Add(poly);

        // Dots + labels
        for (int i = 0; i < rows.Count; i++)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = Palette[0] };
            Canvas.SetLeft(dot, points[i].X - 3.5);
            Canvas.SetTop(dot,  points[i].Y - 3.5);
            ChartCanvas.Children.Add(dot);

            var xLbl = new TextBlock
            {
                Text = rows[i].X.Length > 10 ? rows[i].X[..10] + "…" : rows[i].X,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68))
            };
            Canvas.SetLeft(xLbl, points[i].X - 20);
            Canvas.SetTop(xLbl,  padT + chartH + 6);
            ChartCanvas.Children.Add(xLbl);
        }

        DrawLine2(padL, padT, padL, padT + chartH, Brushes.Gray, 1);
        DrawLine2(padL, padT + chartH, padL + chartW, padT + chartH, Brushes.Gray, 1);
    }

    // ── Pie Chart ─────────────────────────────────────────────

    private void DrawPie(List<(string X, double Y)> rows, double w, double h)
    {
        double cx = w * 0.4, cy = h / 2, r = Math.Min(cx, cy) * 0.75;
        double total = rows.Sum(r2 => r2.Y);
        if (total == 0) return;

        double startAngle = -Math.PI / 2;
        for (int i = 0; i < rows.Count; i++)
        {
            double sweep = 2 * Math.PI * rows[i].Y / total;
            DrawSlice(cx, cy, r, startAngle, sweep, Palette[i % Palette.Length]);

            // Legend
            var dot = new Ellipse { Width = 10, Height = 10, Fill = Palette[i % Palette.Length] };
            double legendY = 40 + i * 22;
            Canvas.SetLeft(dot, w * 0.78);
            Canvas.SetTop(dot, legendY);
            ChartCanvas.Children.Add(dot);

            var pct  = rows[i].Y / total * 100;
            var lbl  = new TextBlock
            {
                Text = $"{rows[i].X}  {pct:F1}%",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };
            Canvas.SetLeft(lbl, w * 0.78 + 14);
            Canvas.SetTop(lbl, legendY - 1);
            ChartCanvas.Children.Add(lbl);

            startAngle += sweep;
        }
    }

    private void DrawSlice(double cx, double cy, double r,
                           double startAngle, double sweep, Brush fill)
    {
        double x1 = cx + r * Math.Cos(startAngle);
        double y1 = cy + r * Math.Sin(startAngle);
        double x2 = cx + r * Math.Cos(startAngle + sweep);
        double y2 = cy + r * Math.Sin(startAngle + sweep);
        bool  large = sweep > Math.PI;

        var fig = new PathFigure { StartPoint = new Point(cx, cy) };
        fig.Segments.Add(new LineSegment(new Point(x1, y1), false));
        fig.Segments.Add(new ArcSegment(
            new Point(x2, y2), new Size(r, r), 0, large,
            SweepDirection.Clockwise, true));
        fig.IsClosed = true;

        var pg   = new PathGeometry(new[] { fig });
        var path = new System.Windows.Shapes.Path
        {
            Data   = pg, Fill = fill,
            Stroke = Brushes.White, StrokeThickness = 1.5
        };
        ChartCanvas.Children.Add(path);

        // Percentage label inside slice
        if (sweep > 0.3)
        {
            double midAngle = startAngle + sweep / 2;
            double lx = cx + r * 0.65 * Math.Cos(midAngle);
            double ly = cy + r * 0.65 * Math.Sin(midAngle);
            var pctLbl = new TextBlock
            {
                Text = $"{sweep / (2 * Math.PI) * 100:F0}%",
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(pctLbl, lx - 12);
            Canvas.SetTop(pctLbl, ly - 8);
            ChartCanvas.Children.Add(pctLbl);
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private void DrawLine2(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };
        ChartCanvas.Children.Add(line);
    }

    private static bool IsNumericColumn(DataTable dt, string col)
    {
        foreach (DataRow row in dt.Rows)
        {
            var v = row[col]?.ToString();
            if (!string.IsNullOrEmpty(v))
                return double.TryParse(v, out _);
        }
        return false;
    }

    private static double? TryParseDouble(string? s) =>
        double.TryParse(s, out var d) ? d : null;

    private static string FormatNumber(double v) =>
        v >= 1_000_000 ? $"{v / 1_000_000:F1}M"
        : v >= 1_000   ? $"{v / 1_000:F1}K"
        :                $"{v:G4}";
}
