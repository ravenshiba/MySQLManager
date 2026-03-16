using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MySQLManager.Models;
using MySQLManager.Services;
using MySQLManager.ViewModels;
using WpfColDef = System.Windows.Controls.ColumnDefinition;

namespace MySQLManager.Views;

public partial class ErdWindow : Window
{
    private ErdDiagram?    _diagram;
    private readonly string _database;
    private readonly Dictionary<string, Border> _tableControls = new();

    private double _scale = 1.0;
    private const double MinScale = 0.15, MaxScale = 3.0;

    // 畫布平移
    private bool   _isPanning;
    private Point  _panStart;
    private double _panStartTx, _panStartTy;

    // 資料表拖曳
    private Border?   _draggingTable;
    private ErdTable? _draggingModel;
    private Point     _dragStart;
    private double    _dragStartX, _dragStartY;

    public ErdWindow(string database)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        _database      = database;
        TitleText.Text = $"ERD 關聯圖 — {database}";
        Loaded += async (_, _) => await LoadDiagramAsync();
    }

    // ── 資料載入 ──────────────────────────────────────────────

    private async Task LoadDiagramAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        RootCanvas.Visibility   = Visibility.Collapsed;
        try
        {
            var svc  = new ErdService(GetActiveConn());
            _diagram = await svc.BuildAsync(_database);
            DrawDiagram();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"載入失敗：{ex.Message}";
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ── 繪製全部 ──────────────────────────────────────────────

    private void DrawDiagram()
    {
        if (_diagram == null) return;
        TableCanvas.Children.Clear();
        RelationCanvas.Children.Clear();
        _tableControls.Clear();

        foreach (var tbl in _diagram.Tables)   DrawTable(tbl);
        foreach (var rel in _diagram.Relations) DrawRelation(rel);

        RootCanvas.Visibility = Visibility.Visible;
        StatsText.Text = $"{_diagram.Tables.Count} 張表 | {_diagram.Relations.Count} 條關聯";
    }

    // ── 繪製一張資料表 ────────────────────────────────────────

    private void DrawTable(ErdTable tbl)
    {
        const double rowH = ErdTable.RowHeight;
        const double hdrH = ErdTable.HeaderHeight;

        // 標題
        var header = new Border
        {
            Background   = new SolidColorBrush(Color.FromRgb(0x1A, 0x6B, 0xAA)),
            Height       = hdrH,
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Child = new TextBlock
            {
                Text = tbl.Name, Foreground = Brushes.White,
                FontWeight = FontWeights.Bold, FontSize = 13,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        // 欄位列表
        var colsPanel = new StackPanel { Margin = new Thickness(0, hdrH, 0, 0) };
        foreach (var col in tbl.Columns)
        {
            var bg = col.IsPrimary ? Color.FromRgb(0x1C, 0x35, 0x4A) : Color.FromRgb(0x17, 0x26, 0x33);
            var rowGrid = new Grid { Margin = new Thickness(6, 0, 6, 0) };
            rowGrid.ColumnDefinitions.Add(new WpfColDef { Width = new GridLength(18) });
            rowGrid.ColumnDefinitions.Add(new WpfColDef { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new WpfColDef { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = col.Icon, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = col.IsPrimary ? Brushes.Gold : col.IsForeign ? Brushes.SkyBlue : Brushes.Gray
            };
            var name = new TextBlock
            {
                Text = col.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                Foreground = col.IsPrimary
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00))
                    : new SolidColorBrush(Color.FromRgb(0xEC, 0xF0, 0xF1))
            };
            var typeText = new TextBlock
            {
                Text = col.Type, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D))
            };

            Grid.SetColumn(icon, 0); Grid.SetColumn(name, 1); Grid.SetColumn(typeText, 2);
            rowGrid.Children.Add(icon); rowGrid.Children.Add(name); rowGrid.Children.Add(typeText);

            colsPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(bg), Height = rowH,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = rowGrid
            });
        }

        // 外框 Border
        var innerGrid = new Grid();
        innerGrid.Children.Add(colsPanel);
        innerGrid.Children.Add(header);

        var border = new Border
        {
            Width = tbl.Width, Height = tbl.Height,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.SizeAll,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Opacity = 0.5 },
            Child = innerGrid
        };

        Canvas.SetLeft(border, tbl.X);
        Canvas.SetTop(border,  tbl.Y);
        TableCanvas.Children.Add(border);
        _tableControls[tbl.Name] = border;

        // 拖曳事件
        border.MouseLeftButtonDown += (_, e) =>
        {
            _draggingTable = border; _draggingModel = tbl;
            _dragStart  = e.GetPosition(TableCanvas);
            _dragStartX = tbl.X; _dragStartY = tbl.Y;
            border.CaptureMouse(); e.Handled = true;
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (_draggingTable != border) return;
            _draggingTable = null; border.ReleaseMouseCapture();
            RedrawRelations();
        };
        border.MouseMove += (_, e) =>
        {
            if (_draggingTable != border || _draggingModel == null) return;
            var pos = e.GetPosition(TableCanvas);
            tbl.X = _dragStartX + (pos.X - _dragStart.X);
            tbl.Y = _dragStartY + (pos.Y - _dragStart.Y);
            Canvas.SetLeft(border, tbl.X); Canvas.SetTop(border, tbl.Y);
            RedrawRelations();
        };
    }

    // ── 繪製關聯線 ────────────────────────────────────────────

    private void DrawRelation(ErdRelation rel)
    {
        if (_diagram == null) return;
        var from = _diagram.Tables.FirstOrDefault(t => t.Name == rel.FromTable);
        var to   = _diagram.Tables.FirstOrDefault(t => t.Name == rel.ToTable);
        if (from == null || to == null) return;

        var (p1, p2) = GetConnectionPoints(from, to);

        // 連線
        RelationCanvas.Children.Add(new Line
        {
            X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
            Stroke = new SolidColorBrush(Color.FromRgb(0x5D, 0xAD, 0xE2)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 6, 3 }
        });

        // 箭頭
        AddArrowHead(p1, p2);

        // 標籤
        var lbl = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x2A, 0x35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5D, 0xAD, 0xE2)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = $"{rel.FromColumn} → {rel.ToColumn}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0xAD, 0xE2)),
                FontSize = 9
            }
        };
        Canvas.SetLeft(lbl, (p1.X + p2.X) / 2 - 30);
        Canvas.SetTop(lbl,  (p1.Y + p2.Y) / 2 - 10);
        RelationCanvas.Children.Add(lbl);
    }

    private void AddArrowHead(Point from, Point to)
    {
        var dx = to.X - from.X; var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        var ux = dx / len; var uy = dy / len;
        const double aLen = 10, aAng = 0.4;
        RelationCanvas.Children.Add(new Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x5D, 0xAD, 0xE2)),
            Points = new PointCollection
            {
                to,
                new(to.X - aLen*(ux*Math.Cos(aAng)  - uy*Math.Sin(aAng)),
                    to.Y - aLen*(uy*Math.Cos(aAng)  + ux*Math.Sin(aAng))),
                new(to.X - aLen*(ux*Math.Cos(-aAng) - uy*Math.Sin(-aAng)),
                    to.Y - aLen*(uy*Math.Cos(-aAng) + ux*Math.Sin(-aAng)))
            }
        });
    }

    private void RedrawRelations()
    {
        if (_diagram == null) return;
        RelationCanvas.Children.Clear();
        foreach (var rel in _diagram.Relations) DrawRelation(rel);
    }

    private static (Point, Point) GetConnectionPoints(ErdTable from, ErdTable to)
    {
        var fcx = from.X + from.Width / 2; var fcy = from.Y + from.Height / 2;
        var tcx = to.X   + to.Width   / 2; var tcy = to.Y   + to.Height  / 2;
        Point p1, p2;
        if (Math.Abs(fcx - tcx) > Math.Abs(fcy - tcy))
        {
            p1 = fcx < tcx ? new(from.X + from.Width, fcy) : new(from.X, fcy);
            p2 = fcx < tcx ? new(to.X, tcy) : new(to.X + to.Width, tcy);
        }
        else
        {
            p1 = fcy < tcy ? new(fcx, from.Y + from.Height) : new(fcx, from.Y);
            p2 = fcy < tcy ? new(tcx, to.Y) : new(tcx, to.Y + to.Height);
        }
        return (p1, p2);
    }

    // ── 工具列按鈕 ────────────────────────────────────────────

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)  => SetScale(_scale * 1.2);
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => SetScale(_scale / 1.2);
    private void BtnReset_Click(object sender, RoutedEventArgs e)
    { SetScale(1.0); TranslateT.X = 0; TranslateT.Y = 0; }

    private void BtnFit_Click(object sender, RoutedEventArgs e)
    {
        if (_diagram?.Tables.Count == 0) return;
        var maxX = _diagram!.Tables.Max(t => t.X + t.Width)  + 40;
        var maxY = _diagram.Tables.Max(t => t.Y + t.Height) + 40;
        SetScale(Math.Min(CanvasBorder.ActualWidth / maxX, CanvasBorder.ActualHeight / maxY) * 0.95);
        TranslateT.X = 10; TranslateT.Y = 10;
    }

    private void SetScale(double s)
    {
        _scale = Math.Clamp(s, MinScale, MaxScale);
        ScaleT.ScaleX = ScaleT.ScaleY = _scale;
        ZoomLabel.Text = $"{_scale * 100:F0}%";
    }

    // ── 畫布平移 ──────────────────────────────────────────────

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var f   = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        var mp  = e.GetPosition(CanvasBorder);
        var ns  = Math.Clamp(_scale * f, MinScale, MaxScale);
        var sd  = ns / _scale;
        TranslateT.X = mp.X - sd * (mp.X - TranslateT.X);
        TranslateT.Y = mp.Y - sd * (mp.Y - TranslateT.Y);
        SetScale(ns); e.Handled = true;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is Border b && _tableControls.ContainsValue(b)) return;
        _isPanning = true;
        _panStart  = e.GetPosition(CanvasBorder);
        _panStartTx = TranslateT.X; _panStartTy = TranslateT.Y;
        CanvasBorder.CaptureMouse();
        CanvasBorder.Cursor = Cursors.Hand;
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        CanvasBorder.ReleaseMouseCapture();
        CanvasBorder.Cursor = Cursors.Arrow;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(CanvasBorder);
        TranslateT.X = _panStartTx + (pos.X - _panStart.X);
        TranslateT.Y = _panStartTy + (pos.Y - _panStart.Y);
    }
    private static MySQLManager.Services.ConnectionService GetActiveConn()
    {
        var vm = System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel;
        return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

    // ══════════════════════════════════════════════════════════════
    // Auto Layout — Fruchterman-Reingold force-directed algorithm
    // ══════════════════════════════════════════════════════════════
    private void BtnAutoLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_diagram == null || _diagram.Tables.Count == 0) return;
        ForceDirectedLayout(_diagram);
        DrawDiagram();
        BtnFit_Click(sender, e);
    }

    private static void ForceDirectedLayout(ErdDiagram diagram)
    {
        var tables = diagram.Tables;
        int n = tables.Count;
        if (n == 0) return;

        const double W = 1400, H = 900;
        const double k = 200.0;   // spring constant
        const int    iterations = 150;
        double temp = 200.0;

        // Random initial placement in a circle
        var rng = new Random(42);
        for (int i = 0; i < n; i++)
        {
            double angle = 2 * Math.PI * i / n;
            tables[i].X = W / 2 + Math.Cos(angle) * 400 + rng.NextDouble() * 20;
            tables[i].Y = H / 2 + Math.Sin(angle) * 300 + rng.NextDouble() * 20;
        }

        var vx = new double[n];
        var vy = new double[n];

        // Build adjacency for edge weight
        var edges = new HashSet<(int, int)>();
        foreach (var rel in diagram.Relations)
        {
            int fi = tables.FindIndex(t => t.Name == rel.FromTable);
            int ti = tables.FindIndex(t => t.Name == rel.ToTable);
            if (fi >= 0 && ti >= 0 && fi != ti) edges.Add((Math.Min(fi, ti), Math.Max(fi, ti)));
        }

        for (int iter = 0; iter < iterations; iter++)
        {
            Array.Clear(vx, 0, n);
            Array.Clear(vy, 0, n);

            // Repulsion between all pairs
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double dx = tables[i].X - tables[j].X;
                double dy = tables[i].Y - tables[j].Y;
                double dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                double f = k * k / dist;
                vx[i] += f * dx / dist;  vy[i] += f * dy / dist;
                vx[j] -= f * dx / dist;  vy[j] -= f * dy / dist;
            }

            // Attraction along edges
            foreach (var (fi, ti) in edges)
            {
                double dx = tables[ti].X - tables[fi].X;
                double dy = tables[ti].Y - tables[fi].Y;
                double dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                double f = dist * dist / k;
                vx[fi] += f * dx / dist;  vy[fi] += f * dy / dist;
                vx[ti] -= f * dx / dist;  vy[ti] -= f * dy / dist;
            }

            // Apply with cooling
            for (int i = 0; i < n; i++)
            {
                double len = Math.Max(1, Math.Sqrt(vx[i] * vx[i] + vy[i] * vy[i]));
                double move = Math.Min(len, temp);
                tables[i].X += vx[i] / len * move;
                tables[i].Y += vy[i] / len * move;
                // Clamp to canvas
                tables[i].X = Math.Max(20, Math.Min(W - tables[i].Width  - 20, tables[i].X));
                tables[i].Y = Math.Max(20, Math.Min(H - tables[i].Height - 20, tables[i].Y));
            }

            temp *= 0.95;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Export PNG
    // ══════════════════════════════════════════════════════════════
    private void BtnExportPng_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "匯出 ERD 為 PNG",
            Filter     = "PNG 圖片 (*.png)|*.png",
            FileName   = $"ERD_{_database}_{DateTime.Now:yyyyMMdd_HHmm}.png",
            DefaultExt = ".png"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            // Render the canvas to a bitmap
            var canvas = RootCanvas;
            var bounds = new System.Windows.Rect(canvas.RenderSize);

            // Include some padding
            const double pad = 20;
            double w = Math.Max(100, canvas.ActualWidth  + pad * 2);
            double h = Math.Max(100, canvas.ActualHeight + pad * 2);

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)w, (int)h, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);

            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // White background
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null,
                    new System.Windows.Rect(0, 0, w, h));
                var vb = new System.Windows.Media.VisualBrush(CanvasBorder);
                dc.DrawRectangle(vb, null, new System.Windows.Rect(pad, pad,
                    CanvasBorder.ActualWidth, CanvasBorder.ActualHeight));
            }
            rtb.Render(dv);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

            using var stream = System.IO.File.OpenWrite(dlg.FileName);
            encoder.Save(stream);

            StatusText.Text = $"✅ 已匯出 PNG：{System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ 匯出失敗：{ex.Message}";
        }
    }

}
