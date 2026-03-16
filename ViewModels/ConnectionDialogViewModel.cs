using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using MySQLManager.Helpers;
using MySQLManager.Models;
using MySQLManager.Services;

namespace MySQLManager.ViewModels;

public class ConnectionDialogViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;

    public ObservableCollection<ConnectionProfile> Profiles { get; } = new();

    private ConnectionProfile _editingProfile = new();
    public ConnectionProfile EditingProfile { get => _editingProfile; set => SetProperty(ref _editingProfile, value); }

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; set => SetProperty(ref _isTesting, value); }

    private string _testResult = string.Empty;
    public string TestResult { get => _testResult; set => SetProperty(ref _testResult, value); }

    private bool _testSuccess;
    public bool TestSuccess { get => _testSuccess; set => SetProperty(ref _testSuccess, value); }

    public AsyncRelayCommand TestConnectionCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public RelayCommand NewProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }
    public RelayCommand SelectProfileCommand { get; }

    public event Action? CloseRequested;

    public ConnectionDialogViewModel()
    {
        _settingsService = App.SettingsService;
        LoadProfiles();

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsTesting);
        ConnectCommand        = new AsyncRelayCommand(ConnectAsync);
        NewProfileCommand     = new RelayCommand(NewProfile);
        DeleteProfileCommand  = new RelayCommand(DeleteProfile);
        SelectProfileCommand  = new RelayCommand(SelectProfile);
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in _settingsService.GetProfiles())
            Profiles.Add(p);

        if (Profiles.Count > 0)
            EditingProfile = CloneProfile(Profiles[0]);
        else
            EditingProfile = new ConnectionProfile();
    }

    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestResult = "測試中...";
        try
        {
            var connService = new ConnectionService();
            await connService.TestConnectionAsync(EditingProfile);
            TestResult = "✅ 連線成功！";
            TestSuccess = true;
        }
        catch (Exception ex)
        {
            TestResult = $"❌ {ex.Message}";
            TestSuccess = false;
        }
        finally { IsTesting = false; }
    }

    private async Task ConnectAsync()
    {
        SaveCurrentProfile();
        var mainVm = (Application.Current.MainWindow?.DataContext as MainViewModel);
        if (mainVm != null)
        {
            CloseRequested?.Invoke();
            await mainVm.ConnectWithProfileAsync(EditingProfile);
        }
    }

    private void NewProfile(object? _ = null)
    {
        EditingProfile = new ConnectionProfile { Name = "New Connection" };
    }

    private void DeleteProfile(object? param)
    {
        if (param is ConnectionProfile p)
        {
            _settingsService.DeleteProfile(p.Id);
            Profiles.Remove(p);
        }
    }

    private void SelectProfile(object? param)
    {
        if (param is ConnectionProfile p)
            EditingProfile = CloneProfile(p);
    }

    private static ConnectionProfile CloneProfile(ConnectionProfile src) => new()
    {
        Id = src.Id, Name = src.Name, Host = src.Host, Port = src.Port,
        Username = src.Username,
        // 顯示時解密密碼
        Password = src.SavePassword ? Services.SettingsService.DecryptPassword(src.Password) : src.Password,
        DefaultDatabase = src.DefaultDatabase,
        SavePassword = src.SavePassword, UseSsl = src.UseSsl,
        UseSshTunnel = src.UseSshTunnel, SshHost = src.SshHost,
        SshPort = src.SshPort, SshUsername = src.SshUsername,
    };

    private void SaveCurrentProfile()
    {
        if (EditingProfile.SavePassword)
        {
            var toSave = new ConnectionProfile
            {
                Id = EditingProfile.Id, Name = EditingProfile.Name,
                Host = EditingProfile.Host, Port = EditingProfile.Port,
                Username = EditingProfile.Username,
                Password = Services.SettingsService.EncryptPassword(EditingProfile.Password),
                DefaultDatabase = EditingProfile.DefaultDatabase,
                SavePassword = true, UseSsl = EditingProfile.UseSsl,
                UseSshTunnel = EditingProfile.UseSshTunnel, SshHost = EditingProfile.SshHost,
                SshPort = EditingProfile.SshPort, SshUsername = EditingProfile.SshUsername,
                SshPassword = Services.SettingsService.EncryptPassword(EditingProfile.SshPassword ?? "")
            };
            _settingsService.SaveProfile(toSave);
        }
        else
        {
            var toSave = new ConnectionProfile
            {
                Id = EditingProfile.Id, Name = EditingProfile.Name,
                Host = EditingProfile.Host, Port = EditingProfile.Port,
                Username = EditingProfile.Username, Password = "",
                DefaultDatabase = EditingProfile.DefaultDatabase,
                SavePassword = false, UseSsl = EditingProfile.UseSsl
            };
            _settingsService.SaveProfile(toSave);
        }

        // 重新整理列表
        Profiles.Clear();
        foreach (var p in _settingsService.GetProfiles())
            Profiles.Add(p);
    }
}
