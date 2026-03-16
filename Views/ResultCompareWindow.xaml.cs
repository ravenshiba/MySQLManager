using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MySQLManager.ViewModels;

namespace MySQLManager.Views;

public partial class ResultCompareWindow : Window
{
    private readonly List<ResultSnapshot> _snapshots;

    public ResultCompareWindow(IEnumerable<ResultSnapshot> snapshots)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        _snapshots = snapshots.ToList();
        LeftCombo.ItemsSource  = _snapshots;
        RightCombo.ItemsSource = _snapshots;
        if (_snapshots.Count >= 1) LeftCombo.SelectedIndex  = 0;
        if (_snapshots.Count >= 2) RightCombo.SelectedIndex = 1;
    }

    private void ComboChanged(object s, SelectionChangedEventArgs e)
    {
        if (LeftCombo.SelectedItem is ResultSnapshot left)
        {
            LeftGrid.ItemsSource  = left.Data.DefaultView;
            LeftRowCount.Text = $"（{left.RowCount} 筆）";
        }
        if (RightCombo.SelectedItem is ResultSnapshot right)
        {
            RightGrid.ItemsSource  = right.Data.DefaultView;
            RightRowCount.Text = $"（{right.RowCount} 筆）";
        }
        DiffSummary.Text = "";
    }

    private void HighlightDiff_Click(object s, RoutedEventArgs e)
    {
        if (LeftCombo.SelectedItem is not ResultSnapshot left ||
            RightCombo.SelectedItem is not ResultSnapshot right)
            return;

        int leftRows  = left.Data.Rows.Count;
        int rightRows = right.Data.Rows.Count;
        int diffRows  = Math.Abs(leftRows - rightRows);

        // 欄位比較
        var leftCols  = left.Data.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet();
        var rightCols = right.Data.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet();
        var onlyLeft  = leftCols.Except(rightCols).ToList();
        var onlyRight = rightCols.Except(leftCols).ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append($"左側 {leftRows} 筆，右側 {rightRows} 筆，差異 {diffRows} 筆");
        if (onlyLeft.Count > 0)
            sb.Append($"  |  左獨有欄位：{string.Join(", ", onlyLeft)}");
        if (onlyRight.Count > 0)
            sb.Append($"  |  右獨有欄位：{string.Join(", ", onlyRight)}");

        DiffSummary.Text = sb.ToString();
    }
}
