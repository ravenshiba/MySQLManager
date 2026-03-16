using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MySQLManager.Views;

public partial class ColumnFilterPopup : UserControl
{
    public event Action<HashSet<string>?>? FilterApplied;

    private List<string> _allValues = new();

    public ColumnFilterPopup()
    {
        InitializeComponent();
    }

    public void Populate(IEnumerable<string> distinctValues, HashSet<string>? currentFilter)
    {
        _allValues = distinctValues.OrderBy(v => v).ToList();
        RebuildList(_allValues);

        // Restore current selection
        if (currentFilter != null)
        {
            foreach (ListBoxItem item in ValueList.Items)
                if (currentFilter.Contains(item.Content?.ToString() ?? ""))
                    item.IsSelected = true;
        }
        else
        {
            ValueList.SelectAll();
        }
        UpdateSelectAllState();
    }

    private void RebuildList(IEnumerable<string> values)
    {
        ValueList.Items.Clear();
        foreach (var v in values)
        {
            var item = new ListBoxItem
            {
                Content = string.IsNullOrEmpty(v) ? "(空白)" : v,
                Tag     = v,
            };
            item.IsSelected = true;
            ValueList.Items.Add(item);
        }
    }

    private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
    {
        var term = SearchBox.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(term)
            ? _allValues
            : _allValues.Where(v => v.ToLower().Contains(term)).ToList();
        RebuildList(filtered);
        UpdateSelectAllState();
    }

    private void SelectAll_Checked(object s, RoutedEventArgs e)   => SetAllSelected(true);
    private void SelectAll_Unchecked(object s, RoutedEventArgs e) => SetAllSelected(false);

    private void SetAllSelected(bool selected)
    {
        foreach (ListBoxItem item in ValueList.Items)
            item.IsSelected = selected;
    }

    private void UpdateSelectAllState()
    {
        var all     = ValueList.Items.Count;
        var selected = ValueList.SelectedItems.Count;
        SelectAllChk.IsChecked = selected == all ? true : selected == 0 ? false : null;
    }

    private void Apply_Click(object s, RoutedEventArgs e)
    {
        var selected = ValueList.SelectedItems.Cast<ListBoxItem>()
            .Select(i => i.Tag?.ToString() ?? "").ToHashSet();

        // null = no filter (all selected)
        FilterApplied?.Invoke(selected.Count == _allValues.Count ? null : selected);
    }

    private void Clear_Click(object s, RoutedEventArgs e)
    {
        ValueList.SelectAll();
        FilterApplied?.Invoke(null);
    }
}
