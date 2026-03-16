using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class ScheduledBackupWindow : Window
{
    private readonly ScheduledBackupService _svc = App.ScheduledBackupService;
    private BackupSchedule? _current;
    private bool _isNew;
    private readonly ObservableCollection<BackupSchedule> _items = new();

    public ScheduledBackupWindow() { InitializeComponent(); Loaded += (_, _) => App.FitWindowToScreen(this); }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 載入資料庫清單
        var dbs = await App.ConnectionService.GetDatabasesAsync();
        DbCombo.ItemsSource = dbs;

        RefreshList();
        FreqCombo.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        var schedules = _svc.GetSchedules().ToList();
        ScheduleList.ItemsSource = schedules;
        EmptyScheduleHint.Visibility =
            schedules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScheduleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScheduleList.SelectedItem is BackupSchedule s)
            LoadSchedule(s, isNew: false);
    }

    private void LoadSchedule(BackupSchedule s, bool isNew)
    {
        _current = s;
        _isNew   = isNew;

        DbCombo.SelectedItem = s.Database;
        if (DbCombo.SelectedItem == null) DbCombo.Text = s.Database;

        FreqCombo.SelectedIndex = s.Frequency switch
        {
            BackupFrequency.Daily   => 0,
            BackupFrequency.Weekly  => 1,
            BackupFrequency.Hourly  => 2,
            _                       => 0
        };
        WeekdayCombo.SelectedIndex = ((int)s.WeekDay + 6) % 7; // Mon=0
        TimeBox.Text        = s.TimeOfDay.ToString(@"hh\:mm");
        OutputDirBox.Text   = s.OutputDir;
        DdlCheck.IsChecked  = s.IncludeDdl;
        DataCheck.IsChecked = s.IncludeData;
        KeepBox.Text        = s.KeepCount.ToString();
        EnabledCheck.IsChecked = s.IsEnabled;
        UpdateNextRunLabel(s);

        RightEmptyHint.Visibility = Visibility.Collapsed;
        EditPanel.Visibility      = Visibility.Visible;
    }

    private void UpdateNextRunLabel(BackupSchedule s)
        => NextRunLabel.Text = s.NextRun.HasValue
            ? s.NextRun.Value.ToString("yyyy/MM/dd HH:mm")
            : "（尚未計算）";

    private void FreqCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FreqCombo.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString() ?? "Daily";
        WeekdayRow.Visibility = tag == "Weekly" ? Visibility.Visible : Visibility.Collapsed;
        TimeRow.Visibility    = tag == "Hourly"  ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddSchedule_Click(object sender, RoutedEventArgs e)
        => LoadSchedule(new BackupSchedule(), isNew: true);

    private void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (DbCombo.SelectedItem == null && string.IsNullOrWhiteSpace(DbCombo.Text))
        { MessageBox.Show("請選擇資料庫。", "提示"); return; }
        if (string.IsNullOrWhiteSpace(OutputDirBox.Text))
        { MessageBox.Show("請填寫備份目錄。", "提示"); return; }

        _current.Database    = DbCombo.SelectedItem?.ToString() ?? DbCombo.Text;
        _current.OutputDir   = OutputDirBox.Text.Trim();
        _current.IncludeDdl  = DdlCheck.IsChecked == true;
        _current.IncludeData = DataCheck.IsChecked == true;
        _current.IsEnabled   = EnabledCheck.IsChecked == true;
        _current.KeepCount   = int.TryParse(KeepBox.Text, out var k) ? k : 7;

        var freqTag = (FreqCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";
        _current.Frequency = freqTag switch
        {
            "Weekly" => BackupFrequency.Weekly,
            "Hourly" => BackupFrequency.Hourly,
            _        => BackupFrequency.Daily
        };

        if (TimeSpan.TryParse(TimeBox.Text, out var ts)) _current.TimeOfDay = ts;

        var wdTag = (WeekdayCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0";
        _current.WeekDay = (DayOfWeek)(int.TryParse(wdTag, out var wd) ? wd : 0);

        if (_isNew) { _svc.Add(_current); _isNew = false; }
        else        _svc.Update(_current);

        UpdateNextRunLabel(_current);
        RefreshList();
        MessageBox.Show("排程已儲存！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (MessageBox.Show($"確定刪除「{_current.Database}」的備份排程？", "確認",
            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _svc.Delete(_current.Id);
        _current = null;
        EditPanel.Visibility      = Visibility.Collapsed;
        RightEmptyHint.Visibility = Visibility.Visible;
        RefreshList();
    }

    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleList.SelectedItem is not BackupSchedule s) return;
        var log = await _svc.ExecuteBackupAsync(s);
        RefreshList();
        var msg = log.Success
            ? $"✅ 備份成功！\n檔案：{log.FilePath}\n大小：{log.SizeLabel}"
            : $"❌ 備份失敗：{log.Message}";
        MessageBox.Show(msg, "備份結果");
    }

    private void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        // .NET 8 WPF 內建 OpenFolderDialog，不需要 WinForms
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title            = "選擇備份儲存目錄",
            InitialDirectory = OutputDirBox.Text
        };
        if (dialog.ShowDialog() == true)
            OutputDirBox.Text = dialog.FolderName;
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        var logs  = _svc.GetLogs();
        var lines = string.Join("\n", logs.Take(50).Select(l =>
            $"[{l.Time:MM/dd HH:mm}] {(l.Success ? "✅" : "❌")} {l.Database} · {l.SizeLabel} · {l.Message}"));
        MessageBox.Show(string.IsNullOrEmpty(lines) ? "尚無備份記錄" : lines,
            "備份記錄（最近 50 筆）", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
