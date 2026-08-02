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

    /// <summary>복구 키 문자열까지 함께 돌려주는 변형(잠긴 내보내기 검증용, TD-050).</summary>
    private static (VaultManager Manager, string RecoveryCode) UnlockedWithRecovery()
    {
        var m = new VaultManager(new InMemoryStore(), Path);
        var key = m.CreateNew(Master, Light);
        return (m, RecoveryCode.Encode(key));
    }

    /// <summary>프롬프트 응답을 미리 정해두고 호출을 기록하는 가짜 다이얼로그.</summary>
    private sealed class FakeDialog : Services.IDialogService
    {
        public string? PromptResult { get; set; }
        public int PromptCount { get; private set; }
        public string? LastNotifyMessage { get; private set; }

        public Task<bool> ConfirmAsync(string t, string m, string c, string x) => Task.FromResult(true);

        public void Notify(string title, string message) => LastNotifyMessage = message;

        public Task<string?> PromptAsync(string title, string message, string placeholder,
            string confirmText, string cancelText)
        {
            PromptCount++;
            return Task.FromResult(PromptResult);
        }
    }

    // ── 복구 키로 잠근 내보내기·가져오기 (TD-050) ──

    [Fact]
    public async Task ExportEncrypted_prompts_for_key_and_hands_sealed_bytes_to_the_view()
    {
        var (manager, code) = UnlockedWithRecovery();
        manager.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "s3cr3t" });
        var dialog = new FakeDialog { PromptResult = code };
        var vm = new SettingsViewModel(manager, dialog);
        byte[]? handed = null;
        vm.ExportEncryptedReady += (_, bytes) => handed = bytes;

        await vm.ExportEncryptedCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.PromptCount);
        Assert.NotNull(handed);
        Assert.DoesNotContain("s3cr3t", System.Text.Encoding.UTF8.GetString(handed!));
    }

    [Fact]
    public async Task ExportEncrypted_cancelled_prompt_does_nothing()
    {
        var (manager, _) = UnlockedWithRecovery();
        var dialog = new FakeDialog { PromptResult = null }; // 취소
        var vm = new SettingsViewModel(manager, dialog);
        var raised = false;
        vm.ExportEncryptedReady += (_, _) => raised = true;

        await vm.ExportEncryptedCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Null(dialog.LastNotifyMessage); // 취소는 오류가 아니다 — 잔소리하지 않는다
    }

    [Fact]
    public async Task ExportEncrypted_wrong_key_notifies_and_produces_no_file()
    {
        var (manager, _) = UnlockedWithRecovery();
        var (_, otherCode) = UnlockedWithRecovery();
        var dialog = new FakeDialog { PromptResult = otherCode };
        var vm = new SettingsViewModel(manager, dialog);
        var raised = false;
        vm.ExportEncryptedReady += (_, _) => raised = true;

        await vm.ExportEncryptedCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.NotNull(dialog.LastNotifyMessage);
    }

    [Fact]
    public async Task PerformEncryptedImport_adds_entries_with_the_files_key()
    {
        var (source, code) = UnlockedWithRecovery();
        source.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "s3cr3t" });
        var file = source.ExportEncrypted(code);

        var (target, _) = UnlockedWithRecovery();
        var dialog = new FakeDialog { PromptResult = code };
        var vm = new SettingsViewModel(target, dialog);

        await vm.PerformEncryptedImportAsync(file);

        Assert.Equal("Steam", Assert.Single(target.Entries).Title);
    }

    [Fact]
    public async Task PerformEncryptedImport_wrong_key_notifies_and_imports_nothing()
    {
        var (source, code) = UnlockedWithRecovery();
        source.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "s3cr3t" });
        var file = source.ExportEncrypted(code);

        var (target, targetCode) = UnlockedWithRecovery();
        var dialog = new FakeDialog { PromptResult = targetCode };
        var vm = new SettingsViewModel(target, dialog);

        await vm.PerformEncryptedImportAsync(file);

        Assert.Empty(target.Entries);
        Assert.NotNull(dialog.LastNotifyMessage);
    }

    [Fact]
    public async Task PerformEncryptedImport_rejects_a_plain_csv_file()
    {
        var (target, code) = UnlockedWithRecovery();
        var plain = System.Text.Encoding.UTF8.GetBytes("title,url,login,password,notes,tags\r\n");
        var dialog = new FakeDialog { PromptResult = code };
        var vm = new SettingsViewModel(target, dialog);

        await vm.PerformEncryptedImportAsync(plain);

        Assert.Empty(target.Entries);
        Assert.NotNull(dialog.LastNotifyMessage); // 암호 문제가 아니라 형식 문제로 안내
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
    public void ImportEncrypted_command_raises_request()
    {
        var vm = new SettingsViewModel(Unlocked(), new FakeDialog());
        var raised = false;
        vm.ImportEncryptedRequested += (_, _) => raised = true;

        vm.ImportEncryptedCommand.Execute(null);

        Assert.True(raised);
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
