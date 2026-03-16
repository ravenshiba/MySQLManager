using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MySQLManager.Services;
using MySQLManager.ViewModels;

namespace MySQLManager.Views
{
    // ── MW_ Converters ────────────────────────────────────────────────────
    public class MW_BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is Visibility x && x == Visibility.Visible;
    }
    public class MW_InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is Visibility x && x == Visibility.Collapsed;
    }
    public class MW_InverseBoolConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is bool b ? !b : (object)false;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is bool b ? !b : (object)false;
    }
    public class MW_NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }
    public class MW_NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => string.IsNullOrEmpty(v?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }
    public class MW_StringToBrushConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
        {
            var hex = v?.ToString();
            if (string.IsNullOrEmpty(hex)) return Brushes.Gray;
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Gray; }
        }
        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }

    // ── MonitorWindow partial class ───────────────────────────────────────
    public partial class MonitorWindow : Window
    {
        private MonitorViewModel Vm => (MonitorViewModel)DataContext;

        public MonitorWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => App.FitWindowToScreen(this);
            DataContext = new MonitorViewModel(GetActiveConn());
            AutoBtn.Content = Vm.AutoLabel;
        }

        private async void Refresh_Click(object s, RoutedEventArgs e) => await Vm.RefreshAsync();

        private void ToggleAuto_Click(object s, RoutedEventArgs e)
        {
            if (Vm.IsRunning) { Vm.StopAuto();  AutoBtn.Content = "▶ 啟動自動更新"; }
            else              { Vm.StartAuto(); AutoBtn.Content = "⏹ 停止更新"; }
        }

        private async void KillProcess_Click(object s, RoutedEventArgs e)
        {
            if (ProcessGrid.SelectedItem is not ProcessInfo p) return;
            var res = MessageBox.Show($"確定要 KILL 進程 {p.Id}（{p.User}）？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            await GetActiveConn().KillProcessAsync(p.Id);
            await Vm.RefreshAsync();
        }

        protected override void OnClosed(EventArgs e) { Vm.Dispose(); base.OnClosed(e); }

        private static ConnectionService GetActiveConn()
        {
            var vm = Application.Current.MainWindow?.DataContext as MainViewModel;
            return vm?.ActiveSession?.ConnectionService ?? App.ConnectionService;
        }
    }
}
