using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.Models;
using MySQLManager.Services;

namespace MySQLManager.Views;

public partial class CrossConnectWindow : Window
{
    private readonly List<ConnectionProfile> _profiles;
    private ConnectionService? _connA;
    private ConnectionService? _connB;

    public CrossConnectWindow()
    {
        InitializeComponent();
        _profiles = App.SettingsService.GetProfiles();

        Loaded += (_, _) =>
        {
            App.FitWindowToScreen(this);
            PopulateConnections();
        };
    }

    private void PopulateConnections()
    {
        foreach (var p in _profiles)
        {
            ConnACombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
            ConnBCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
        }
        if (ConnACombo.Items.Count > 0) ConnACombo.SelectedIndex = 0;
        if (ConnBCombo.Items.Count > 1) ConnBCombo.SelectedIndex = 1;
        else if (ConnBCombo.Items.Count > 0) ConnBCombo.SelectedIndex = 0;
    }

    private async void RunBoth_Click(object s, RoutedEventArgs e)
    {
        var sql = SqlEditor.Text?.Trim();
        if (string.IsNullOrEmpty(sql)) { StatusLabel.Text = "請輸入 SQL"; return; }

        var profA = (ConnACombo.SelectedItem as ComboBoxItem)?.Tag as ConnectionProfile;
        var profB = (ConnBCombo.SelectedItem as ComboBoxItem)?.Tag as ConnectionProfile;
        if (profA == null || profB == null) { StatusLabel.Text = "請選擇兩個連線"; return; }

        StatusLabel.Text = "執行中…";
        GridA.ItemsSource = null;
        GridB.ItemsSource = null;

        try
        {
            // Connect both
            _connA = new ConnectionService();
            _connB = new ConnectionService();

            var taskA = Task.Run(async () =>
            {
                await _connA.ConnectAsync(profA);
                return await _connA.ExecuteQueryAsync(sql);
            });
            var taskB = Task.Run(async () =>
            {
                await _connB.ConnectAsync(profB);
                return await _connB.ExecuteQueryAsync(sql);
            });

            await Task.WhenAll(taskA, taskB);

            var resultA = taskA.Result;
            var resultB = taskB.Result;

            Dispatcher.Invoke(() =>
            {
                if (resultA.Data != null)
                {
                    GridA.ItemsSource = resultA.Data.DefaultView;
                    CountA.Text = $"（{resultA.Data.Rows.Count:N0} 行）";
                }
                else CountA.Text = $"❌ {resultA.ErrorMessage}";

                if (resultB.Data != null)
                {
                    GridB.ItemsSource = resultB.Data.DefaultView;
                    CountB.Text = $"（{resultB.Data.Rows.Count:N0} 行）";
                }
                else CountB.Text = $"❌ {resultB.ErrorMessage}";

                StatusLabel.Text = $"✅ 完成  A: {resultA.Data?.Rows.Count ?? 0} 行  " +
                                   $"B: {resultB.Data?.Rows.Count ?? 0} 行";
            });
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"❌ {ex.Message}";
        }
    }

    private void DiffResults_Click(object s, RoutedEventArgs e)
    {
        var dtA = (GridA.ItemsSource as DataView)?.Table;
        var dtB = (GridB.ItemsSource as DataView)?.Table;
        if (dtA == null || dtB == null) { StatusLabel.Text = "請先執行查詢"; return; }

        // Highlight rows that differ
        int diffCount = 0;
        int minRows = Math.Min(dtA.Rows.Count, dtB.Rows.Count);

        // Mark rows using simple string comparison
        for (int i = 0; i < minRows; i++)
        {
            var rowA = string.Join("|", dtA.Rows[i].ItemArray.Select(x => x?.ToString() ?? ""));
            var rowB = string.Join("|", dtB.Rows[i].ItemArray.Select(x => x?.ToString() ?? ""));
            if (rowA != rowB) diffCount++;
        }

        int extraA = Math.Max(0, dtA.Rows.Count - minRows);
        int extraB = Math.Max(0, dtB.Rows.Count - minRows);

        FooterLabel.Text = $"差異分析：{diffCount} 行內容不同 | " +
                          $"A 比 B 多 {extraA} 行 | B 比 A 多 {extraB} 行";

        StatusLabel.Text = diffCount == 0 && extraA == 0 && extraB == 0
            ? "✅ 兩個結果完全相同" : $"⚠️ 發現差異（共 {diffCount + extraA + extraB} 處）";
    }

    protected override void OnClosed(EventArgs e)
    {
        _connA?.Dispose();
        _connB?.Dispose();
        base.OnClosed(e);
    }
}
