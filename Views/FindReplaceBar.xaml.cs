using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace MySQLManager.Views;

public partial class FindReplaceBar : UserControl
{
    private TextEditor? _editor;
    private int _lastMatchOffset = -1;

    public FindReplaceBar()
    {
        InitializeComponent();
    }

    public void Attach(TextEditor editor)
    {
        _editor = editor;
    }

    public void Open(bool replaceMode = false)
    {
        Visibility = Visibility.Visible;
        ReplaceLabel.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        ReplaceBox.Visibility   = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        ReplaceOneBtn.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        ReplaceAllBtn.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;

        // Pre-fill with selected text
        if (_editor != null && !string.IsNullOrEmpty(_editor.SelectedText))
            FindBox.Text = _editor.SelectedText;

        FindBox.Focus();
        FindBox.SelectAll();
        HighlightAll();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        ClearHighlights();
        _editor?.Focus();
    }

    // ── Search logic ─────────────────────────────────────────
    private Regex? BuildRegex()
    {
        var pattern = FindBox.Text;
        if (string.IsNullOrEmpty(pattern)) return null;
        var opts = RegexOptions.None;
        if (!MatchCaseChk.IsChecked == true) opts |= RegexOptions.IgnoreCase;
        if (WholeWordChk.IsChecked == true)  pattern = $@"\b{Regex.Escape(pattern)}\b";
        else                                  pattern = Regex.Escape(pattern);
        try { return new Regex(pattern, opts); }
        catch { return null; }
    }

    private void HighlightAll()
    {
        if (_editor == null) return;
        var re = BuildRegex();
        var text = _editor.Text;
        if (re == null || string.IsNullOrEmpty(text))
        {
            CountLabel.Text = "";
            return;
        }
        var matches = re.Matches(text);
        CountLabel.Text = matches.Count == 0
            ? "找不到"
            : $"{matches.Count} 個符合";
    }

    private void FindNext(bool backwards = false)
    {
        if (_editor == null) return;
        var re = BuildRegex();
        if (re == null) return;
        var text  = _editor.Text;
        int start = _editor.CaretOffset;

        if (!backwards)
        {
            var m = re.Match(text, start);
            if (!m.Success) m = re.Match(text, 0);   // wrap
            if (m.Success) Select(m.Index, m.Length);
        }
        else
        {
            var all = re.Matches(text);
            if (all.Count == 0) return;
            Match? best = null;
            foreach (Match m in all)
                if (m.Index < start - 1) best = m;
            best ??= all[all.Count - 1];             // wrap to last
            Select(best.Index, best.Length);
        }
    }

    private void Select(int offset, int length)
    {
        if (_editor == null) return;
        _editor.Focus();
        _editor.Select(offset, length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(offset).LineNumber);
        _lastMatchOffset = offset;
    }

    // ── Replace ───────────────────────────────────────────────
    private void ReplaceOne()
    {
        if (_editor == null) return;
        var re = BuildRegex();
        if (re == null) return;
        // If current selection matches, replace it
        var sel = _editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && re.IsMatch(sel))
        {
            var rep = re.Replace(sel, ReplaceBox.Text);
            _editor.Document.Replace(_editor.SelectionStart, sel.Length, rep);
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        if (_editor == null) return;
        var re = BuildRegex();
        if (re == null) return;
        var original = _editor.Text;
        var replaced = re.Replace(original, ReplaceBox.Text);
        if (replaced == original) return;
        int count = re.Matches(original).Count;
        _editor.Document.Text = replaced;
        CountLabel.Text = $"已取代 {count} 處";
        _lastMatchOffset = -1;
    }

    // ── Highlight (transform search layer) ───────────────────
    private void ClearHighlights() { /* AvalonEdit SearchPanel 可選接 */ }

    // ── Events ───────────────────────────────────────────────
    private void FindBox_TextChanged(object s, TextChangedEventArgs e) => HighlightAll();
    private void Option_Changed(object s, RoutedEventArgs e)           => HighlightAll();
    private void FindNext_Click(object s, RoutedEventArgs e)  => FindNext(false);
    private void FindPrev_Click(object s, RoutedEventArgs e)  => FindNext(true);
    private void ReplaceOne_Click(object s, RoutedEventArgs e) => ReplaceOne();
    private void ReplaceAll_Click(object s, RoutedEventArgs e) => ReplaceAll();
    private void Close_Click(object s, RoutedEventArgs e)      => Close();

    private void FindBox_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift) FindNext(true);
            else FindNext(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
