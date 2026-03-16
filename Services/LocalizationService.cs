using System;
using System.Globalization;
using System.Windows;

namespace MySQLManager.Services;

public enum AppLanguage { ZhTW, En, Ja }

public class LocalizationService
{
    private const string PrefKey = "language";
    private readonly SettingsService _settings;

    public AppLanguage Current { get; private set; } = AppLanguage.ZhTW;

    public event Action<AppLanguage>? LanguageChanged;

    public LocalizationService(SettingsService settings)
    {
        _settings = settings;
        // Load saved preference
        var saved = _settings.GetString(PrefKey, "zh-TW");
        Current = saved switch {
            "en"    => AppLanguage.En,
            "ja"    => AppLanguage.Ja,
            _       => AppLanguage.ZhTW,
        };
    }

    public void Apply()
    {
        var dictUri = Current switch {
            AppLanguage.En   => new Uri("/Resources/Localization/Strings.en.xaml",    UriKind.Relative),
            AppLanguage.Ja   => new Uri("/Resources/Localization/Strings.ja.xaml",    UriKind.Relative),
            _                => new Uri("/Resources/Localization/Strings.zh-TW.xaml", UriKind.Relative),
        };

        var dict = new ResourceDictionary { Source = dictUri };
        var merged = Application.Current.Resources.MergedDictionaries;

        // Replace existing localization dictionary
        for (int i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.OriginalString ?? "";
            if (src.Contains("/Localization/Strings."))
            {
                merged[i] = dict;
                LanguageChanged?.Invoke(Current);
                return;
            }
        }
        merged.Add(dict);
        LanguageChanged?.Invoke(Current);
    }

    public void SetLanguage(AppLanguage lang)
    {
        if (Current == lang) return;
        Current = lang;
        Apply();
        _settings.SetString(PrefKey, lang switch {
            AppLanguage.En => "en",
            AppLanguage.Ja => "ja",
            _              => "zh-TW",
        });
    }

    public string LanguageLabel => Current switch {
        AppLanguage.En => "EN",
        AppLanguage.Ja => "日",
        _              => "繁",
    };
}
