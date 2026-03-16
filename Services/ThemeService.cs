using System;
using System.Windows;
using Newtonsoft.Json;
using System.IO;

namespace MySQLManager.Services;

public enum AccentTheme { Blue, Green, Purple, Orange, Red }

public class ThemeService
{
    private const string LightUri = "/Resources/Styles/AppStyles.xaml";
    private const string DarkUri  = "/Resources/Styles/DarkTheme.xaml";
    private readonly string _prefPath;

    public bool        IsDark  { get; private set; }
    public AccentTheme Accent  { get; private set; } = AccentTheme.Blue;
    public event Action<bool>? ThemeChanged;

    private static string AccentUri(AccentTheme a) => a == AccentTheme.Blue ? "" :
        $"/Resources/Styles/Accent{a}.xaml";

    public ThemeService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MySQLManager");
        Directory.CreateDirectory(dir);
        _prefPath = Path.Combine(dir, "theme.json");
        LoadPref();
    }

    private void LoadPref()
    {
        try
        {
            if (File.Exists(_prefPath))
            {
                var json = File.ReadAllText(_prefPath);
                var pref = JsonConvert.DeserializeObject<dynamic>(json);
                IsDark = (bool)(pref?.dark ?? false);
                if (Enum.TryParse<AccentTheme>((string)(pref?.accent ?? "Blue"), out var a))
                    Accent = a;
            }
        }
        catch { }
    }

    public void Apply()
    {
        var uri  = new Uri(IsDark ? DarkUri : LightUri, UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        // 找到 App.Resources 裡的 AppStyles/DarkTheme，替換它
        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.OriginalString ?? "";
            if (src.Contains("AppStyles") || src.Contains("DarkTheme"))
            {
                merged[i] = dict;
                ThemeChanged?.Invoke(IsDark);
                return;
            }
        }
        // 若找不到，直接加入
        merged.Add(dict);
        ThemeChanged?.Invoke(IsDark);
    }

    public void Toggle()
    {
        IsDark = !IsDark;
        Apply();
        SavePref();
    }

    public void SetDark(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        Apply();
        SavePref();
    }

    public void SetAccent(AccentTheme accent)
    {
        if (Accent == accent) return;
        Accent = accent;
        Apply();
        SavePref();
    }

    private void SavePref()
    {
        try { File.WriteAllText(_prefPath, JsonConvert.SerializeObject(
            new { dark = IsDark, accent = Accent.ToString() })); }
        catch { }
    }
}
