using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class SettingsViewModelTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Master = "correct horse battery staple";
    private const string Path = "vault.dat";

    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) => _files[path] = vault;
        public EncryptedVault Load(string path) => _files[path];
    }

    private static VaultManager Unlocked()
    {
        var m = new VaultManager(new InMemoryStore(), Path);
        m.CreateNew(Master, Light);
        return m;
    }

    [Fact]
    public void SetupOtp_raises_OtpSetupRequested()
    {
        var vm = new SettingsViewModel(Unlocked());
        var raised = false;
        vm.OtpSetupRequested += (_, _) => raised = true;

        vm.SetupOtpCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void ChangeMasterPassword_raises_ChangeMasterRequested()
    {
        var vm = new SettingsViewModel(Unlocked());
        var raised = false;
        vm.ChangeMasterRequested += (_, _) => raised = true;

        vm.ChangeMasterPasswordCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void ReissueRecovery_raises_ReissueRecoveryRequested()
    {
        var vm = new SettingsViewModel(Unlocked());
        var raised = false;
        vm.ReissueRecoveryRequested += (_, _) => raised = true;

        vm.ReissueRecoveryCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void Backup_and_Restore_commands_raise_requests()
    {
        var vm = new SettingsViewModel(Unlocked());
        var backup = false; var restore = false;
        vm.BackupRequested += (_, _) => backup = true;
        vm.RestoreRequested += (_, _) => restore = true;

        vm.BackupCommand.Execute(null);
        vm.RestoreCommand.Execute(null);

        Assert.True(backup);
        Assert.True(restore);
    }

    [Fact]
    public void PerformBackup_then_PerformRestore_roundtrips_and_locks()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path, Light);
        m.CreateNew(Master, Light);
        m.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var vm = new SettingsViewModel(m);
        var locked = false;
        vm.Locked += (_, _) => locked = true;

        vm.PerformBackup("backup.dat");
        m.Add(new VaultEntry { Title = "Later", Login = "x", Password = "pw" }); // 백업 이후 변경
        vm.PerformRestore("backup.dat");

        Assert.True(locked);            // 복원 후 잠금 화면으로
        m.Open(Master);
        Assert.Equal("Steam", Assert.Single(m.Entries).Title);
    }

    [Fact]
    public void Export_and_Import_commands_raise_requests()
    {
        var vm = new SettingsViewModel(Unlocked());
        var export = false; var import = false;
        vm.ExportRequested += (_, _) => export = true;
        vm.ImportRequested += (_, _) => import = true;

        vm.ExportCommand.Execute(null);
        vm.ImportCommand.Execute(null);

        Assert.True(export);
        Assert.True(import);
    }

    [Fact]
    public void BuildExportCsv_returns_current_entries_as_csv()
    {
        var m = Unlocked();
        m.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var vm = new SettingsViewModel(m);

        var csv = vm.BuildExportCsv();

        Assert.Contains("title,url,login,password,notes,tags", csv);
        Assert.Contains("Steam", csv);
    }

    [Fact]
    public void PerformImport_adds_entries_and_reports_count()
    {
        var m = Unlocked();
        var vm = new SettingsViewModel(m);
        var csv = "title,url,login,password,notes,tags\r\nSteam,,gamer,pw,,\r\n";

        var count = vm.PerformImport(csv);

        Assert.Equal(1, count);
        Assert.Single(m.Entries);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void Loads_time_settings_from_vault()
    {
        var m = Unlocked();
        m.SetTimeSettings(7, 25);

        var vm = new SettingsViewModel(m);

        Assert.Equal(7, vm.AutoLockMinutes);
        Assert.Equal(25, vm.ClipboardClearSeconds);
    }

    [Fact]
    public void SaveTimeSettings_persists_clamps_and_raises_changed()
    {
        var m = Unlocked();
        var vm = new SettingsViewModel(m) { AutoLockMinutes = 0, ClipboardClearSeconds = 9999 };
        var changed = false;
        vm.TimeSettingsChanged += (_, _) => changed = true;

        vm.SaveTimeSettingsCommand.Execute(null);

        Assert.True(changed);
        Assert.Equal(1, vm.AutoLockMinutes);        // 하한 클램프
        Assert.Equal(300, vm.ClipboardClearSeconds); // 상한 클램프
        Assert.Equal(1, m.AutoLockMinutes);
        Assert.Equal(300, m.ClipboardClearSeconds);
    }

    [Fact]
    public void IsOtpRegistered_reflects_vault_state_after_refresh()
    {
        var manager = Unlocked();
        var vm = new SettingsViewModel(manager);
        Assert.False(vm.IsOtpRegistered);

        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        vm.Refresh();

        Assert.True(vm.IsOtpRegistered);
    }

    // --- 슬랙·네트워크 설정 (M6 S5, design 7.8·7.9) ---

    [Fact]
    public void Loads_network_and_slack_defaults_from_vault()
    {
        var vm = new SettingsViewModel(Unlocked());

        Assert.False(vm.NetworkAllowed);   // 기본 오프라인
        Assert.False(vm.SlackEnabled);     // 기본 OFF
        Assert.Equal(PasswordManager.Core.Models.SlackSettings.DefaultTemplate, vm.MessageTemplate);
    }

    [Fact]
    public void SaveSlackSettings_persists_and_raises_event()
    {
        var manager = Unlocked();
        var vm = new SettingsViewModel(manager)
        {
            NetworkAllowed = true,
            SlackEnabled = true,
            SlackWebhookUrl = "  https://hooks.slack.com/services/x  ", // 트림 확인
            IncludeSiteName = true,
        };
        var raised = false;
        vm.NetworkSettingsChanged += (_, _) => raised = true;

        vm.SaveSlackSettingsCommand.Execute(null);

        Assert.True(raised);
        Assert.True(manager.NetworkAllowed);
        Assert.True(manager.Slack.Enabled);
        Assert.Equal("https://hooks.slack.com/services/x", manager.Slack.WebhookUrl);
        Assert.True(manager.Slack.IncludeSiteName);
    }

    [Fact]
    public void ResetTemplate_restores_default()
    {
        var vm = new SettingsViewModel(Unlocked()) { MessageTemplate = "바뀐 문구" };

        vm.ResetTemplateCommand.Execute(null);

        Assert.Equal(PasswordManager.Core.Models.SlackSettings.DefaultTemplate, vm.MessageTemplate);
    }

    [Fact]
    public void TemplatePreview_renders_event_label()
    {
        var vm = new SettingsViewModel(Unlocked()) { MessageTemplate = "{이벤트}!" };

        Assert.Equal("잠금 해제됨!", vm.TemplatePreview);
    }

    [Fact]
    public void Empty_template_saves_as_default()
    {
        var manager = Unlocked();
        var vm = new SettingsViewModel(manager) { MessageTemplate = "   " };

        vm.SaveSlackSettingsCommand.Execute(null);

        Assert.Equal(PasswordManager.Core.Models.SlackSettings.DefaultTemplate, manager.Slack.MessageTemplate);
    }
}
