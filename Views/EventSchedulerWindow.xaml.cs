using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Models;

namespace MySQLManager.Views;

public partial class EventSchedulerWindow : Window
{
    private readonly string _database;
    private ObservableCollection<MySqlEvent> _events = new();
    private MySqlEvent? _editing;

    public EventSchedulerWindow(string database)
    {
        InitializeComponent();
        _database = database;
        Title = $"📅 MySQL Event 排程器 — {database}";
        Loaded += async (_, _) =>
        {
            App.FitWindowToScreen(this);
            await LoadEventsAsync();
        };
    }

    // ── Load ─────────────────────────────────────────────────
    private async System.Threading.Tasks.Task LoadEventsAsync()
    {
        try
        {
            var list = await App.ConnectionService.GetEventsAsync(_database);
            _events = new ObservableCollection<MySqlEvent>(list);
            EventList.ItemsSource = _events;
            EditorStatus.Text = $"共 {_events.Count} 個 Event";
        }
        catch (Exception ex)
        {
            EditorStatus.Text = $"❌ {ex.Message}";
        }
    }

    // ── List selection ────────────────────────────────────────
    private void EventList_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (EventList.SelectedItem is MySqlEvent ev) LoadEditor(ev);
    }

    private void LoadEditor(MySqlEvent ev)
    {
        _editing = ev;
        NameBox.Text = ev.Name;
        BodyBox.Text = ev.Definition;

        // Type
        foreach (ComboBoxItem item in TypeCombo.Items)
            if (item.Tag?.ToString() == ev.EventType) { TypeCombo.SelectedItem = item; break; }

        // Status
        foreach (ComboBoxItem item in StatusCombo.Items)
            if (item.Tag?.ToString() == ev.Status) { StatusCombo.SelectedItem = item; break; }

        // Interval
        IntervalValueBox.Text = ev.IntervalValue;
        foreach (ComboBoxItem item in IntervalFieldCombo.Items)
            if (item.Content?.ToString() == ev.IntervalField) { IntervalFieldCombo.SelectedItem = item; break; }

        // One-time
        if (ev.ExecuteAt.HasValue)
        {
            ExecuteDatePicker.SelectedDate = ev.ExecuteAt.Value.Date;
            ExecuteTimeBox.Text = ev.ExecuteAt.Value.ToString("HH:mm:ss");
        }

        // Starts / Ends
        StartsPicker.SelectedDate = ev.Starts?.Date;
        EndsPicker.SelectedDate   = ev.Ends?.Date;

        // Completion
        foreach (ComboBoxItem item in CompletionCombo.Items)
            if (item.Tag?.ToString() == ev.OnCompletion) { CompletionCombo.SelectedItem = item; break; }

        UpdatePanelVisibility();
    }

    private void TypeCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
        => UpdatePanelVisibility();

    private void UpdatePanelVisibility()
    {
        bool isRecurring = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "RECURRING";
        RecurringPanel.Visibility  = isRecurring ? Visibility.Visible : Visibility.Collapsed;
        StartsEndsPanel.Visibility = isRecurring ? Visibility.Visible : Visibility.Collapsed;
        OneTimePanel.Visibility    = isRecurring ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── New Event ─────────────────────────────────────────────
    private void NewEvent_Click(object s, RoutedEventArgs e)
    {
        _editing = null;
        EventList.SelectedItem = null;
        NameBox.Text           = "new_event";
        BodyBox.Text           = "BEGIN\n  -- SQL here\nEND";
        IntervalValueBox.Text  = "1";
        ExecuteDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        ExecuteTimeBox.Text    = "00:00:00";
        StartsPicker.SelectedDate = DateTime.Today;
        EndsPicker.SelectedDate   = null;
        TypeCombo.SelectedIndex    = 0;
        StatusCombo.SelectedIndex  = 0;
        CompletionCombo.SelectedIndex = 0;
        UpdatePanelVisibility();
        NameBox.Focus();
        NameBox.SelectAll();
        EditorStatus.Text = "新增模式：填寫後按「儲存 Event」";
    }

    // ── Save ─────────────────────────────────────────────────
    private async void Save_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { MessageBox.Show("請輸入 Event 名稱"); return; }
        if (string.IsNullOrWhiteSpace(BodyBox.Text)) { MessageBox.Show("請輸入 Event 主體 SQL"); return; }

        var ev = new MySqlEvent
        {
            Name          = NameBox.Text.Trim(),
            Definition    = BodyBox.Text.Trim(),
            EventType     = (TypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "RECURRING",
            Status        = (StatusCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ENABLED",
            OnCompletion  = (CompletionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "NOT PRESERVE",
            IntervalValue = IntervalValueBox.Text.Trim(),
            IntervalField = (IntervalFieldCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "HOUR",
            Starts        = StartsPicker.SelectedDate,
            Ends          = EndsPicker.SelectedDate,
        };

        if (ev.EventType == "ONE TIME" && ExecuteDatePicker.SelectedDate.HasValue)
        {
            if (TimeSpan.TryParse(ExecuteTimeBox.Text, out var t))
                ev.ExecuteAt = ExecuteDatePicker.SelectedDate.Value.Date + t;
        }

        try
        {
            var result = await App.ConnectionService.CreateOrReplaceEventAsync(_database, ev);
            if (result.ErrorMessage != null)
            {
                EditorStatus.Text = $"❌ {result.ErrorMessage}";
                return;
            }
            EditorStatus.Text = $"✅ Event '{ev.Name}' 已儲存";
            await LoadEventsAsync();

            // Re-select saved event
            var saved = _events.FirstOrDefault(x => x.Name == ev.Name);
            if (saved != null) EventList.SelectedItem = saved;
        }
        catch (Exception ex)
        {
            EditorStatus.Text = $"❌ {ex.Message}";
        }
    }

    // ── Enable / Disable / Delete ─────────────────────────────
    private async void Enable_Click(object s, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is not MySqlEvent ev) return;
        await App.ConnectionService.SetEventStatusAsync(_database, ev.Name, true);
        await LoadEventsAsync();
    }

    private async void Disable_Click(object s, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is not MySqlEvent ev) return;
        await App.ConnectionService.SetEventStatusAsync(_database, ev.Name, false);
        await LoadEventsAsync();
    }

    private async void Delete_Click(object s, RoutedEventArgs e)
    {
        if (EventList.SelectedItem is not MySqlEvent ev) return;
        var res = MessageBox.Show($"確定刪除 Event '{ev.Name}'？", "確認刪除",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        await App.ConnectionService.DropEventAsync(_database, ev.Name);
        await LoadEventsAsync();
        NewEvent_Click(s, e);
    }

    private async void Refresh_Click(object s, RoutedEventArgs e)
        => await LoadEventsAsync();

    // ── Copy SQL ──────────────────────────────────────────────
    private void CopySql_Click(object s, RoutedEventArgs e)
    {
        var sql = $"-- Event: {NameBox.Text}\n{BodyBox.Text}";
        Clipboard.SetText(sql);
        EditorStatus.Text = "✅ SQL 已複製到剪貼板";
    }
}
