using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MySQLManager.Views;

public partial class JsonViewerDialog : Window
{
    public string ResultJson { get; private set; } = "";

    public JsonViewerDialog(string json)
    {
        InitializeComponent();
        Loaded += (_, _) => App.FitWindowToScreen(this);
        LoadJson(json);
    }

    // ── Load ──────────────────────────────────────────────────
    private void LoadJson(string json)
    {
        RawBox.TextChanged -= RawBox_TextChanged;
        try
        {
            var doc = JsonDocument.Parse(json);
            RawBox.Text = FormatJson(json);
            JsonTree.Items.Clear();
            JsonTree.Items.Add(BuildNode("root", doc.RootElement));
            ExpandFirstLevel();
            StatusLabel.Text = $"✅ 有效 JSON";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 142, 117));
        }
        catch (JsonException ex)
        {
            RawBox.Text = json;
            StatusLabel.Text = $"❌ 無效 JSON：{ex.Message}";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(200, 50, 50));
        }
        finally
        {
            RawBox.TextChanged += RawBox_TextChanged;
        }
    }

    // ── Tree building ─────────────────────────────────────────
    private TreeViewItem BuildNode(string key, JsonElement el)
    {
        var item = new TreeViewItem { FontFamily = new FontFamily("Consolas"), FontSize = 12 };

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var hasProps = el.EnumerateObject().GetEnumerator().MoveNext();
                item.Header = MakeHeader(key, hasProps ? "{ ... }" : "{ }", "#569CD6");
                foreach (var prop in el.EnumerateObject())
                    item.Items.Add(BuildNode(prop.Name, prop.Value));
                break;

            case JsonValueKind.Array:
                item.Header = MakeHeader(key, $"[ {el.GetArrayLength()} items ]", "#569CD6");
                int idx = 0;
                foreach (var child in el.EnumerateArray())
                    item.Items.Add(BuildNode($"[{idx++}]", child));
                break;

            case JsonValueKind.String:
                item.Header = MakeHeader(key, $"\"{el.GetString()}\"", "#CE9178");
                break;

            case JsonValueKind.Number:
                item.Header = MakeHeader(key, el.GetRawText(), "#B5CEA8");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                item.Header = MakeHeader(key, el.GetRawText(), "#569CD6");
                break;

            case JsonValueKind.Null:
                item.Header = MakeHeader(key, "null", "#808080");
                break;

            default:
                item.Header = MakeHeader(key, el.GetRawText(), "#D4D4D4");
                break;
        }

        item.Tag = el.GetRawText();
        return item;
    }

    private static StackPanel MakeHeader(string key, string value, string valueColor)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(key))
        {
            sp.Children.Add(new TextBlock
            {
                Text = key + ": ",
                Foreground = new SolidColorBrush(Color.FromRgb(156, 220, 254)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            });
        }
        sp.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(valueColor)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        });
        return sp;
    }

    private void ExpandFirstLevel()
    {
        foreach (TreeViewItem item in JsonTree.Items)
        {
            item.IsExpanded = true;
            foreach (TreeViewItem child in item.Items)
                child.IsExpanded = true;
        }
    }

    // ── Format helpers ────────────────────────────────────────
    private static string FormatJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private static string CompactJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch { return json; }
    }

    // ── Events ───────────────────────────────────────────────
    private void Copy_Click(object s, RoutedEventArgs e)
        => Clipboard.SetText(RawBox.Text);

    private void Format_Click(object s, RoutedEventArgs e)
    {
        var formatted = FormatJson(RawBox.Text);
        LoadJson(formatted);
    }

    private void Compact_Click(object s, RoutedEventArgs e)
    {
        var compact = CompactJson(RawBox.Text);
        LoadJson(compact);
    }

    private bool _updating;
    private void RawBox_TextChanged(object s, TextChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try { LoadJson(RawBox.Text); }
        finally { _updating = false; }
    }

    private void JsonTree_SelectedItemChanged(object s, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item)
            PathLabel.Text = BuildPath(item);
    }

    private static string BuildPath(TreeViewItem item)
    {
        var parts = new List<string>();
        var cur = item;
        while (cur != null)
        {
            if (cur.Header is StackPanel sp &&
                sp.Children.Count > 0 &&
                sp.Children[0] is TextBlock tb)
                parts.Insert(0, tb.Text.TrimEnd(':', ' '));
            cur = cur.Parent as TreeViewItem;
        }
        return "$." + string.Join(".", parts);
    }

    private void Ok_Click(object s, RoutedEventArgs e)
    {
        ResultJson = RawBox.Text;
        DialogResult = true;
    }
}
