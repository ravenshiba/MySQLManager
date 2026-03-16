using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using MySQLManager.Models;
using Renci.SshNet;

namespace MySQLManager.Services;

/// <summary>
/// 建立 SSH Tunnel，將遠端 MySQL port 轉發到本機隨機 port
/// </summary>
public class SshTunnelService : IDisposable
{
    private SshClient?         _sshClient;
    private ForwardedPortLocal? _forwardedPort;

    public bool   IsActive   => _sshClient?.IsConnected == true && _forwardedPort?.IsStarted == true;
    public uint   LocalPort  { get; private set; }
    public string StatusText { get; private set; } = string.Empty;

    /// <summary>
    /// 建立 SSH Tunnel，成功後回傳本機 port
    /// </summary>
    public async Task<uint> StartAsync(ConnectionProfile profile)
    {
        if (!profile.UseSshTunnel)
            throw new InvalidOperationException("Profile 未啟用 SSH Tunnel");

        await Task.Run(() =>
        {
            // 建立認證方式
            AuthenticationMethod auth = profile.SshUseKeyAuth && File.Exists(profile.SshKeyPath)
                ? new PrivateKeyAuthenticationMethod(
                    profile.SshUsername,
                    new PrivateKeyFile(profile.SshKeyPath,
                        string.IsNullOrEmpty(profile.SshPassword) ? null : profile.SshPassword))
                : new PasswordAuthenticationMethod(profile.SshUsername, profile.SshPassword);

            var connInfo = new ConnectionInfo(
                profile.SshHost, profile.SshPort, profile.SshUsername, auth);
            connInfo.Timeout = TimeSpan.FromSeconds(15);

            _sshClient = new SshClient(connInfo);
            _sshClient.Connect();

            // 隨機找一個本機閒置 port
            LocalPort = GetFreePort();

            _forwardedPort = new ForwardedPortLocal(
                "127.0.0.1", LocalPort,
                profile.Host, (uint)profile.Port);

            _sshClient.AddForwardedPort(_forwardedPort);
            _forwardedPort.Start();
        });

        StatusText = $"SSH Tunnel: {profile.SshUsername}@{profile.SshHost} → 127.0.0.1:{LocalPort}";
        return LocalPort;
    }

    public void Stop()
    {
        try
        {
            _forwardedPort?.Stop();
            _sshClient?.Disconnect();
        }
        catch { }
        StatusText = string.Empty;
    }

    private static uint GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = (uint)((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose() => Stop();
}
