using System;

namespace MySQLManager.Models;

/// <summary>
/// 儲存一組 MySQL 連線設定
/// </summary>
public class ConnectionProfile
{
    public string Group      { get; set; } = "預設";
    public bool   IsFavorite { get; set; }
    public int    SortOrder  { get; set; }
    public string Notes      { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Connection";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Username { get; set; } = "root";
    public string Password { get; set; } = string.Empty;
    public string DefaultDatabase { get; set; } = string.Empty;
    public bool SavePassword { get; set; } = false;
    public bool   UseSsl          { get; set; } = false;
    public bool   SslVerifyServer { get; set; } = false;
    public string SslCaCert       { get; set; } = "";
    public string SslClientCert   { get; set; } = "";
    public string SslClientKey    { get; set; } = "";

    // ── SSH Tunnel ────────────────────────────────────────────
    public bool UseSshTunnel    { get; set; } = false;
    public string SshHost       { get; set; } = string.Empty;
    public int    SshPort       { get; set; } = 22;
    public string SshUsername   { get; set; } = string.Empty;
    public string SshPassword   { get; set; } = string.Empty;
    public string SshKeyPath    { get; set; } = string.Empty;  // 私鑰檔路徑
    public bool   SshUseKeyAuth { get; set; } = false;         // true=私鑰, false=密碼

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastConnectedAt { get; set; }

    public string ConnectionString =>
        $"Server={Host};Port={Port};Database={DefaultDatabase};" +
        $"Uid={Username};Pwd={Password};" +
        $"SslMode={(UseSsl ? (SslVerifyServer ? "VerifyCA" : "Required") : "None")};" +
        (UseSsl && !string.IsNullOrEmpty(SslCaCert) ? $"SslCa={SslCaCert};" : "") +
        (UseSsl && !string.IsNullOrEmpty(SslClientCert) ? $"SslCert={SslClientCert};" : "") +
        (UseSsl && !string.IsNullOrEmpty(SslClientKey) ? $"SslKey={SslClientKey};" : "") +
        $"AllowPublicKeyRetrieval=True;";

    public override string ToString() => Name;
}
