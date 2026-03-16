using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MySQLManager.Views;

public partial class BlobPreviewDialog : Window
{
    private readonly byte[] _data;
    private readonly string _columnName;
    private bool _isImage;

    public BlobPreviewDialog(byte[] data, string columnName = "BLOB")
    {
        InitializeComponent();
        _data       = data;
        _columnName = columnName;
        Title       = $"🖼 BLOB 預覽 — {columnName}";
        Loaded += (_, _) =>
        {
            App.FitWindowToScreen(this);
            Analyse();
        };
    }

    // ── Detect and render ─────────────────────────────────────
    private void Analyse()
    {
        var kb   = _data.Length / 1024.0;
        var size = kb < 1024 ? $"{kb:F1} KB" : $"{kb / 1024:F2} MB";
        InfoLabel.Text = $"{_columnName}  ·  {_data.Length:N0} bytes ({size})  ·  {DetectMime()}";

        _isImage = TryLoadImage();
        if (!_isImage)
        {
            ImageModeBtn.IsEnabled = false;
            ImageModeBtn.IsChecked = false;
            HexModeBtn.IsChecked   = true;
        }
        RenderCurrentMode();
    }

    private bool TryLoadImage()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource    = new MemoryStream(_data);
            bmp.CacheOption     = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            PreviewImage.Source = bmp;
            return true;
        }
        catch { return false; }
    }

    private string DetectMime()
    {
        if (_data.Length < 4) return "binary";
        // PNG
        if (_data[0] == 0x89 && _data[1] == 0x50) return "image/png";
        // JPEG
        if (_data[0] == 0xFF && _data[1] == 0xD8) return "image/jpeg";
        // GIF
        if (_data[0] == 0x47 && _data[1] == 0x49) return "image/gif";
        // WebP
        if (_data.Length > 12 && _data[8] == 0x57 && _data[9] == 0x45) return "image/webp";
        // PDF
        if (_data[0] == 0x25 && _data[1] == 0x50) return "application/pdf";
        // ZIP
        if (_data[0] == 0x50 && _data[1] == 0x4B) return "application/zip";
        // UTF-8 text heuristic
        if (IsLikelyText()) return "text/plain";
        return "application/octet-stream";
    }

    private bool IsLikelyText()
    {
        int check = Math.Min(_data.Length, 512);
        int printable = 0;
        for (int i = 0; i < check; i++)
        {
            byte b = _data[i];
            if (b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127)) printable++;
        }
        return (double)printable / check > 0.80;
    }

    // ── Mode switch ───────────────────────────────────────────
    private void Mode_Changed(object s, RoutedEventArgs e) => RenderCurrentMode();

    private void RenderCurrentMode()
    {
        ImageScroll.Visibility      = Visibility.Collapsed;
        HexBox.Visibility           = Visibility.Collapsed;
        BinaryPlaceholder.Visibility= Visibility.Collapsed;

        if (ImageModeBtn.IsChecked == true && _isImage)
        {
            ImageScroll.Visibility = Visibility.Visible;
        }
        else if (HexModeBtn.IsChecked == true)
        {
            HexBox.Text       = BuildHexDump(_data, 512);
            HexBox.Visibility = Visibility.Visible;
        }
        else if (TextModeBtn.IsChecked == true)
        {
            try
            {
                HexBox.Text = Encoding.UTF8.GetString(_data);
            }
            catch
            {
                HexBox.Text = Encoding.Latin1.GetString(_data);
            }
            HexBox.Visibility = Visibility.Visible;
        }
        else
        {
            BlobSizeLabel.Text         = $"{_data.Length:N0} bytes";
            BinaryPlaceholder.Visibility= Visibility.Visible;
        }
    }

    private static string BuildHexDump(byte[] data, int maxRows = 512)
    {
        var sb  = new StringBuilder();
        int rows = Math.Min((data.Length + 15) / 16, maxRows);
        for (int row = 0; row < rows; row++)
        {
            int offset = row * 16;
            sb.Append($"{offset:X8}  ");
            for (int i = 0; i < 16; i++)
            {
                if (offset + i < data.Length) sb.Append($"{data[offset + i]:X2} ");
                else sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append("  ");
            for (int i = 0; i < 16 && offset + i < data.Length; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            sb.AppendLine();
        }
        if (data.Length > maxRows * 16)
            sb.AppendLine($"\n… 僅顯示前 {maxRows * 16:N0} bytes，共 {data.Length:N0} bytes");
        return sb.ToString();
    }

    // ── Toolbar actions ───────────────────────────────────────
    private void SaveAs_Click(object s, RoutedEventArgs e)
    {
        var mime = DetectMime();
        var ext  = mime.Contains("png") ? ".png"
                 : mime.Contains("jpeg") ? ".jpg"
                 : mime.Contains("gif") ? ".gif"
                 : mime.Contains("webp") ? ".webp"
                 : mime.Contains("pdf") ? ".pdf"
                 : mime.Contains("zip") ? ".zip"
                 : mime.Contains("text") ? ".txt"
                 : ".bin";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "另存 BLOB 為檔案",
            FileName   = _columnName + ext,
            DefaultExt = ext,
            Filter     = $"偵測到的格式 (*{ext})|*{ext}|所有檔案 (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllBytes(dlg.FileName, _data);
            InfoLabel.Text = $"✅ 已儲存至 {dlg.FileName}";
        }
    }

    private void CopyHex_Click(object s, RoutedEventArgs e)
    {
        var hex = BitConverter.ToString(_data).Replace("-", " ");
        Clipboard.SetText(hex);
        InfoLabel.Text = "✅ Hex 已複製";
    }

    private void CopyBase64_Click(object s, RoutedEventArgs e)
    {
        Clipboard.SetText(Convert.ToBase64String(_data));
        InfoLabel.Text = "✅ Base64 已複製";
    }

    private void Close_Click(object s, RoutedEventArgs e) => Close();
}
