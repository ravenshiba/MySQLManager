using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySQLManager.Models;

namespace MySQLManager.Services;

/// <summary>
/// 儲存/讀取連線設定 (存於 AppData)
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "MySQLManager");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        _settings = Load();
    }

    public List<ConnectionProfile> GetProfiles() => _settings.Profiles;

    public void SaveProfile(ConnectionProfile profile)
    {
        var existing = _settings.Profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0)
            _settings.Profiles[existing] = profile;
        else
            _settings.Profiles.Add(profile);
        Save();
    }

    public void SaveAiApiKey(string key)
    {
        _settings.AiApiKey = key;
        Save();
    }

    public string LoadAiApiKey() => _settings.AiApiKey;

    // ── DPAPI 密碼加解密 (Windows) ────────────────────────────

    /// <summary>將明文密碼加密後儲存</summary>
    public static string EncryptPassword(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        try
        {
            var bytes     = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch { return plainText; } // 非 Windows 時原樣儲存
    }

    /// <summary>解密 DPAPI 加密密碼；若解密失敗回傳原始值（向下相容）</summary>
    public static string DecryptPassword(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return "";
        try
        {
            var bytes     = Convert.FromBase64String(cipherText);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return cipherText; } // 可能是舊版未加密的明文
    }

    public void DeleteProfile(Guid id)
    {
        _settings.Profiles.RemoveAll(p => p.Id == id);
        Save();
    }

    public AppSettings GetSettings() => _settings;
    public void UpdateSettings(AppSettings s) { _settings = s; Save(); }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* 讀取失敗時使用預設值 */ }
        return new AppSettings();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
    public string GetString(string key, string defaultVal = "")
        => key == "language" ? (_settings.Language ?? defaultVal) : defaultVal;

    public void SetString(string key, string value)
    {
        if (key == "language") { _settings.Language = value; Save(); }
    }

}

public class AppSettings
{
    public List<ConnectionProfile> Profiles { get; set; } = new();
    public string Language  { get; set; } = "zh-TW";
    public string AiApiKey { get; set; } = string.Empty;
    public string Theme { get; set; } = "Dark";
    public string FontFamily { get; set; } = "Consolas";
    public int FontSize { get; set; } = 13;
    public int MaxQueryRows { get; set; } = 1000;

}
