using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class DashboardWindow : Window
{
    private ConnectionService           _conn = null!;
    private CancellationTokenSource?    _cts;
    private bool                        _running = true;
    private int                         _intervalSec = 5;

    // 最多保留 60 個資料點
    private const int MaxPoints = 60;
    private readonly List<double> _qpsData  = new();
    private readonly List<double> _connData = new();
    private readonly List<double> _readData = new();
    private readonly List<double> _writeData = new();

    // 前一次快照（用來算 delta）
    private long _prevQueries = -1;
    private long _prevRead    = -1;
    private long _prevWrite   = -1;

    public DashboardWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        _conn = (System.Windows.Application.Current.MainWindow?.DataContext
                 as MySQLManager.ViewModels.MainViewModel)
                ?.ActiveSession?.ConnectionService ?? App.ConnectionService;
    }

    private void Window_Loaded(object s, RoutedEventArgs e) => StartPolling();
    private void Window_Closed(object s, EventArgs e)        => _cts?.Cancel();

    private void StartPolling()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Dispatcher.InvokeAsync(PollAsync); }
                catch { }
                await Task.Delay(_intervalSec * 1000, token).ContinueWith(_ => { });
            }
        }, token);
    }

    private async Task PollAsync()
    {
        try
        {
            var s = await _conn.GetServerStatusAsync();
            var now = DateTime.Now;

            // 計算 delta QPS
            double qps  = _prevQueries < 0 ? 0 : (s.QueriesTotal - _prevQueries) / (double)_intervalSec;
            double reads = _prevRead < 0 ? 0 : (s.InnodbPagesRead - _prevRead) / (double)_intervalSec;
            double writes = _prevWrite < 0 ? 0 : (s.InnodbPagesWritten - _prevWrite) / (double)_intervalSec;
            _prevQueries = s.QueriesTotal;
            _prevRead    = s.InnodbPagesRead;
            _prevWrite   = s.InnodbPagesWritten;

            // 更新卡片
            QpsCard.Text   = qps.ToString("N0");
            ConnCard.Text  = s.ThreadsConnected.ToString();
            ReadCard.Text  = reads.ToString("N0");
            WriteCard.Text = writes.ToString("N0");
            SlowCard.Text  = s.SlowQueries.ToString("N0");
            StatusText.Text = $"上次更新 {now:HH:mm:ss}";

            // 加入歷史
            AddPoint(_qpsData,   qps);
            AddPoint(_connData,  s.ThreadsConnected);
            AddPoint(_readData,  reads);
            AddPoint(_writeData, writes);

            // 繪圖
            DrawChart(QpsCanvas,
                ("QPS",  _qpsData,  Color.FromRgb(0x4D, 0xA3, 0xFF)),
                ("連線", _connData, Color.FromRgb(0x66, 0xBB, 0x6A)));
            DrawChart(IoCanvas,
                ("讀取", _readData,  Color.FromRgb(0xFF, 0xA7, 0x26)),
                ("寫入", _writeData, Color.FromRgb(0xEF, 0x53, 0x50)));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ {ex.Message}";
        }
    }

    private static void AddPoint(List<double> list, double v)
    {
        list.Add(Math.Max(0, v));
        if (list.Count > MaxPoints) list.RemoveAt(0);
    }

    // ── 折線圖繪製 ────────────────────────────────────────────
    private void DrawChart(Canvas canvas,
        (string Label, List<double> Data, Color Clr) s1,
        (string Label, List<double> Data, Color Clr) s2)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 10 || h < 10) return;

        double pad = 28;
        double cw = w - pad;
        double ch = h - pad;

        // 格線
        for (int i = 0; i <= 4; i++)
        {
            double y = pad + ch * i / 4.0;
            var gl = new Line { X1 = pad, Y1 = y, X2 = w, Y2 = y,
                                Stroke = new SolidColorBrush(Color.FromArgb(40, 255,255,255)),
                                StrokeThickness = 1 };
            canvas.Children.Add(gl);
        }

        double maxVal = Math.Max(1,
            s1.Data.Concat(s2.Data).DefaultIfEmpty(1).Max());

        DrawSeries(canvas, s1.Data, s1.Clr, cw, ch, pad, maxVal);
        DrawSeries(canvas, s2.Data, s2.Clr, cw, ch, pad, maxVal);

        // 圖例
        AddLegend(canvas, s1.Label, s1.Clr, 10, 4);
        AddLegend(canvas, s2.Label, s2.Clr, 80, 4);
    }

    private void DrawSeries(Canvas canvas, List<double> data, Color clr,
        double cw, double ch, double pad, double maxVal)
    {
        if (data.Count < 2) return;
        var points = new PointCollection();
        int n = data.Count;
        for (int i = 0; i < n; i++)
        {
            double x = pad + cw * i / (MaxPoints - 1.0);
            double y = pad + ch - ch * data[i] / maxVal;
            points.Add(new System.Windows.Point(x, y));
        }

        // 填色
        var pg = new PathGeometry();
        var pf = new PathFigure { StartPoint = new System.Windows.Point(points[0].X, pad + ch) };
        pf.Segments.Add(new LineSegment(points[0], false));
        for (int i = 1; i < points.Count; i++)
            pf.Segments.Add(new LineSegment(points[i], true));
        pf.Segments.Add(new LineSegment(new System.Windows.Point(points[^1].X, pad + ch), false));
        pg.Figures.Add(pf);
        var fillClr = Color.FromArgb(40, clr.R, clr.G, clr.B);
        canvas.Children.Add(new Path
        {
            Data = pg, Fill = new SolidColorBrush(fillClr)
        });

        // 折線
        var pl = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(clr),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(pl);
    }

    private static void AddLegend(Canvas c, string label, Color clr, double x, double y)
    {
        var r = new Rectangle { Width=10, Height=3, Fill=new SolidColorBrush(clr),
                                 RadiusX=1, RadiusY=1 };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y + 5);
        c.Children.Add(r);
        var tb = new TextBlock { Text=label, FontSize=9,
                                  Foreground=new SolidColorBrush(Colors.LightGray) };
        Canvas.SetLeft(tb, x + 14); Canvas.SetTop(tb, y);
        c.Children.Add(tb);
    }

    // ── 控制 ─────────────────────────────────────────────────
    private void StartStop_Click(object s, RoutedEventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
            _running = false;
            StartStopBtn.Content = "▶ 啟動";
        }
        else
        {
            _running = true;
            StartStopBtn.Content = "⏹ 停止";
            StartPolling();
        }
    }

    private void IntervalCombo_Changed(object s, SelectionChangedEventArgs e)
    {
        if (IntervalCombo.SelectedItem is ComboBoxItem { Tag: string tag }
            && int.TryParse(tag, out int sec))
            _intervalSec = sec;
    }
}
