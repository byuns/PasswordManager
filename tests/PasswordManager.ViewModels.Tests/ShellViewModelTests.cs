using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class ShellViewModelTests
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

    [Fact]
    public void First_run_shows_create_flow()
    {
        var shell = new ShellViewModel(new VaultManager(new InMemoryStore(), Path), Light);

        Assert.Equal(ShellState.Creating, shell.State);
        Assert.IsType<CreateVaultViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void Existing_vault_shows_unlock_flow()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);

        var shell = new ShellViewModel(new VaultManager(store, Path), Light);

        Assert.Equal(ShellState.Unlocking, shell.State);
        Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task AutoLock_when_open_locks_vault_and_returns_to_unlock()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var vault = new VaultManager(store, Path);
        var shell = new ShellViewModel(vault, Light);
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);
        Assert.Equal(ShellState.Open, shell.State);

        shell.AutoLock();

        Assert.False(vault.IsUnlocked);
        Assert.Equal(ShellState.Unlocking, shell.State);
        Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void AutoLock_is_noop_when_not_open()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light); // 언락 화면 상태

        shell.AutoLock(); // 예외 없이 무시

        Assert.Equal(ShellState.Unlocking, shell.State);
    }

    [Fact]
    public async Task Successful_unlock_transitions_to_open()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);

        Assert.Equal(ShellState.Open, shell.State);
    }

    [Fact]
    public async Task Create_then_acknowledge_transitions_to_open()
    {
        var shell = new ShellViewModel(new VaultManager(new InMemoryStore(), Path), Light);

        var create = Assert.IsType<CreateVaultViewModel>(shell.CurrentViewModel);
        create.Password = Master;
        create.ConfirmPassword = Master;
        await create.CreateCommand.ExecuteAsync(null);
        Assert.Equal(ShellState.Creating, shell.State); // 아직 복구 키 확인 전

        create.RecoveryKeySaved = true;
        create.AcknowledgeCommand.Execute(null);

        Assert.Equal(ShellState.Open, shell.State);
    }

    /// <summary>기존 볼트를 언락해 메인 화면까지 진입한 셸을 만든다.</summary>
    private static async Task<(ShellViewModel shell, MainViewModel main)> OpenedShellAsync()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light);
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);
        return (shell, Assert.IsType<MainViewModel>(shell.CurrentViewModel));
    }

    [Fact]
    public async Task Open_shows_main_view_model()
    {
        var (shell, _) = await OpenedShellAsync();

        Assert.Equal(ShellState.Open, shell.State);
        Assert.IsType<MainViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Add_request_opens_editor_and_save_returns_to_same_main()
    {
        var (shell, main) = await OpenedShellAsync();

        main.NewEntryCommand.Execute(null);
        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        Assert.True(editor.IsNew);

        editor.Title = "Steam";
        editor.SaveCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);   // 동일 메인 인스턴스로 복귀
        Assert.Single(main.Entries);                 // 목록 갱신됨
    }

    [Fact]
    public async Task Edit_request_gates_on_otp_then_opens_prefilled_editor()
    {
        var (shell, main, secret) = await OpenedShellWithOtpSecretAsync();
        AddAndSelectEntry(shell, main, "Steam");

        main.EditCommand.Execute(null);

        // 편집도 게이트를 거친다 → OTP 확인 화면
        var gate = Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
        Assert.Equal(OtpGatePurpose.Edit, gate.Purpose);
        gate.VerificationCode = TotpValidator.GenerateCode(secret, DateTimeOffset.UtcNow);
        gate.RevealCommand.Execute(null);

        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        Assert.False(editor.IsNew);
        Assert.Equal("Steam", editor.Title);
    }

    [Fact]
    public async Task Cancel_edit_gate_returns_to_main()
    {
        var (shell, main, _) = await OpenedShellWithOtpSecretAsync();
        AddAndSelectEntry(shell, main, "Steam");

        main.EditCommand.Execute(null);
        var gate = Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
        gate.CancelCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);
    }

    [Fact]
    public async Task Passing_otp_once_grants_both_view_and_edit_until_lock()
    {
        var (shell, main, secret) = await OpenedShellWithOtpSecretAsync();
        var entry = AddAndSelectEntry(shell, main, "Steam");

        // 1) 보기로 OTP 통과
        main.RevealCommand.Execute(null);
        var gate = Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
        gate.VerificationCode = TotpValidator.GenerateCode(secret, DateTimeOffset.UtcNow);
        gate.RevealCommand.Execute(null);
        Assert.Equal(entry.Password, gate.RevealedPassword);
        gate.CancelCommand.Execute(null); // 메인 복귀

        // 2) 같은 항목 편집 → 코드 없이 바로 편집 화면
        main.EditCommand.Execute(null);
        Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);

        // 3) 다시 보기 → 코드 입력 없이 즉시 노출
        shell.ShowItemsCommand.Execute(null);
        main.SelectedEntry = main.Entries[0];
        main.RevealCommand.Execute(null);
        var graceGate = Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
        Assert.False(graceGate.RequiresCode);
        Assert.Equal(entry.Password, graceGate.RevealedPassword);
    }

    [Fact]
    public async Task Grace_is_cleared_after_lock()
    {
        var (shell, main, secret) = await OpenedShellWithOtpSecretAsync();
        AddAndSelectEntry(shell, main, "Steam");
        main.EditCommand.Execute(null);
        var gate = Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
        gate.VerificationCode = TotpValidator.GenerateCode(secret, DateTimeOffset.UtcNow);
        gate.RevealCommand.Execute(null);
        Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel); // 편집 진입(통과 기록됨)

        shell.LockCommand.Execute(null); // 잠금 → 그레이스 해제

        // 다시 언락 후 편집하면 게이트가 또 뜬다
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);
        var main2 = Assert.IsType<MainViewModel>(shell.CurrentViewModel);
        main2.SelectedEntry = main2.Entries[0];
        main2.EditCommand.Execute(null);
        Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Cancel_editor_returns_to_main()
    {
        var (shell, main) = await OpenedShellAsync();
        main.NewEntryCommand.Execute(null);
        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);

        editor.CancelCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);
    }

    [Fact]
    public async Task Lock_returns_to_unlock_flow()
    {
        var (shell, main) = await OpenedShellAsync();

        main.LockCommand.Execute(null);

        Assert.Equal(ShellState.Unlocking, shell.State);
        Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void Forgot_password_shows_recovery_flow()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.ForgotPasswordCommand.Execute(null);

        Assert.IsType<RecoveryViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Successful_recovery_transitions_to_main()
    {
        var store = new InMemoryStore();
        var recoveryKey = new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.ForgotPasswordCommand.Execute(null);
        var recovery = Assert.IsType<RecoveryViewModel>(shell.CurrentViewModel);
        recovery.RecoveryCodeInput = RecoveryCode.Encode(recoveryKey);
        recovery.NewPassword = "new-master";
        recovery.ConfirmPassword = "new-master";
        await recovery.RecoverCommand.ExecuteAsync(null);

        Assert.Equal(ShellState.Open, shell.State);
        Assert.IsType<MainViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void Cancel_recovery_returns_to_unlock()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.ForgotPasswordCommand.Execute(null);
        var recovery = Assert.IsType<RecoveryViewModel>(shell.CurrentViewModel);
        recovery.CancelCommand.Execute(null);

        Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        Assert.Equal(ShellState.Unlocking, shell.State);
    }

    /// <summary>열림 셸에서 설정 화면으로 이동해 SettingsViewModel을 반환한다.</summary>
    private static SettingsViewModel GoToSettings(ShellViewModel shell)
    {
        shell.ShowSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Change_master_request_from_settings_success_returns_to_settings()
    {
        var (shell, _) = await OpenedShellAsync();
        var settings = GoToSettings(shell);

        settings.ChangeMasterPasswordCommand.Execute(null);
        var change = Assert.IsType<ChangeMasterPasswordViewModel>(shell.CurrentViewModel);
        change.CurrentPassword = Master;
        change.NewPassword = "new-master";
        change.ConfirmPassword = "new-master";
        await change.ChangeCommand.ExecuteAsync(null);

        Assert.Same(settings, shell.CurrentViewModel); // 설정으로 복귀
        Assert.Equal(ShellSection.Settings, shell.Section);
    }

    [Fact]
    public async Task Cancel_change_master_returns_to_settings()
    {
        var (shell, _) = await OpenedShellAsync();
        var settings = GoToSettings(shell);

        settings.ChangeMasterPasswordCommand.Execute(null);
        var change = Assert.IsType<ChangeMasterPasswordViewModel>(shell.CurrentViewModel);
        change.CancelCommand.Execute(null);

        Assert.Same(settings, shell.CurrentViewModel);
    }

    [Fact]
    public async Task Settings_restore_locks_and_returns_to_unlock()
    {
        var (shell, _) = await OpenedShellAsync();
        var settings = GoToSettings(shell);

        settings.PerformBackup("backup.dat");
        settings.PerformRestore("backup.dat");

        Assert.Equal(ShellState.Unlocking, shell.State);
        Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        Assert.False(shell.IsSidebarVisible);
    }

    // --- OTP 등록 마법사 / 열람 게이트 내비게이션 (design 5.4·7.4) ---

    /// <summary>셸을 열고 설정에서 OTP 등록 마법사로 OTP를 등록한 뒤 항목 화면으로 복귀한다.</summary>
    private static async Task<(ShellViewModel shell, MainViewModel main)> OpenedShellWithOtpAsync()
    {
        var (shell, main, _) = await OpenedShellWithOtpSecretAsync();
        return (shell, main);
    }

    /// <summary>OTP 등록까지 마친 셸과 함께 등록된 secret을 돌려준다(게이트 코드 생성용).</summary>
    private static async Task<(ShellViewModel shell, MainViewModel main, string secret)> OpenedShellWithOtpSecretAsync()
    {
        var (shell, main) = await OpenedShellAsync();
        var settings = GoToSettings(shell);
        settings.SetupOtpCommand.Execute(null);
        var wizard = Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);
        var secret = wizard.Secret;
        wizard.VerificationCode = TotpValidator.GenerateCode(secret, DateTimeOffset.UtcNow);
        wizard.ConfirmCommand.Execute(null);
        shell.ShowItemsCommand.Execute(null); // 열람 게이트 테스트를 위해 항목으로 복귀
        return (shell, main, secret);
    }

    /// <summary>메인에 항목 하나를 추가하고 선택 상태로 만든다.</summary>
    private static VaultEntry AddAndSelectEntry(ShellViewModel shell, MainViewModel main, string title)
    {
        main.NewEntryCommand.Execute(null);
        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        editor.Title = title;
        editor.Password = "s3cr3t";
        editor.SaveCommand.Execute(null);
        main.SelectedEntry = main.Entries[0];
        return main.Entries[0];
    }

    [Fact]
    public async Task Otp_setup_request_from_settings_opens_wizard()
    {
        var (shell, _) = await OpenedShellAsync();
        var settings = GoToSettings(shell);

        settings.SetupOtpCommand.Execute(null);

        Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Otp_setup_complete_returns_to_settings_and_marks_registered()
    {
        var (shell, _) = await OpenedShellAsync();
        var settings = GoToSettings(shell);
        settings.SetupOtpCommand.Execute(null);
        var wizard = Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);
        wizard.VerificationCode = TotpValidator.GenerateCode(wizard.Secret, DateTimeOffset.UtcNow);
        wizard.ConfirmCommand.Execute(null);

        var returned = Assert.IsType<SettingsViewModel>(shell.CurrentViewModel); // 설정으로 복귀
        Assert.True(returned.IsOtpRegistered);
    }

    [Fact]
    public async Task Otp_resetup_when_registered_requires_master_confirm()
    {
        var (shell, _) = await OpenedShellWithOtpAsync(); // OTP 등록됨 + 항목 화면
        var settings = GoToSettings(shell);

        settings.SetupOtpCommand.Execute(null);

        var confirm = Assert.IsType<MasterConfirmViewModel>(shell.CurrentViewModel);
        confirm.Password = "wrong";
        confirm.ConfirmCommand.Execute(null);
        Assert.IsType<MasterConfirmViewModel>(shell.CurrentViewModel); // 여전히 확인 화면
        Assert.False(string.IsNullOrEmpty(confirm.ErrorMessage));

        confirm.Password = Master;
        confirm.ConfirmCommand.Execute(null);
        Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel); // 통과 → 재설정 마법사
    }

    [Fact]
    public async Task Cancel_master_confirm_returns_to_settings()
    {
        var (shell, _) = await OpenedShellWithOtpAsync();
        var settings = GoToSettings(shell);
        settings.SetupOtpCommand.Execute(null);
        var confirm = Assert.IsType<MasterConfirmViewModel>(shell.CurrentViewModel);

        confirm.CancelCommand.Execute(null);

        Assert.Same(settings, shell.CurrentViewModel);
    }

    [Fact]
    public async Task Cancel_otp_setup_returns_to_settings()
    {
        var (shell, _) = await OpenedShellAsync();
        var settings = GoToSettings(shell);
        settings.SetupOtpCommand.Execute(null);
        var wizard = Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);

        wizard.CancelCommand.Execute(null);

        Assert.Same(settings, shell.CurrentViewModel);
    }

    [Fact]
    public async Task Reveal_request_opens_gate_and_cancel_returns_to_main()
    {
        var (shell, main) = await OpenedShellWithOtpAsync();
        main.NewEntryCommand.Execute(null);
        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        editor.Title = "Steam";
        editor.SaveCommand.Execute(null);
        main.SelectedEntry = main.Entries[0];

        main.RevealCommand.Execute(null);
        var gate = Assert.IsType<OtpGateViewModel>(shell.CurrentViewModel);
        gate.CancelCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);
    }

    // --- 열림 모드 사이드바 네비게이션 (design-ux 2·3절, TD-023) ---

    [Fact]
    public async Task Open_selects_items_section_and_shows_sidebar()
    {
        var (shell, _) = await OpenedShellAsync();

        Assert.Equal(ShellSection.Items, shell.Section);
        Assert.True(shell.IsSidebarVisible);
    }

    [Fact]
    public async Task Show_settings_navigates_to_settings_page()
    {
        var (shell, _) = await OpenedShellAsync();

        shell.ShowSettingsCommand.Execute(null);

        Assert.IsType<SettingsViewModel>(shell.CurrentViewModel);
        Assert.Equal(ShellSection.Settings, shell.Section);
        Assert.True(shell.IsSidebarVisible);
    }

    [Fact]
    public async Task Show_info_navigates_to_info_page()
    {
        var (shell, _) = await OpenedShellAsync();

        shell.ShowInfoCommand.Execute(null);

        Assert.IsType<InfoViewModel>(shell.CurrentViewModel);
        Assert.Equal(ShellSection.Info, shell.Section);
    }

    [Fact]
    public async Task Show_manual_navigates_to_manual_page()
    {
        var (shell, _) = await OpenedShellAsync();

        shell.ShowManualCommand.Execute(null);

        Assert.IsType<ManualViewModel>(shell.CurrentViewModel);
        Assert.Equal(ShellSection.Manual, shell.Section);
        Assert.True(shell.IsSidebarVisible);
    }

    [Fact]
    public async Task Show_info_exposes_injected_version_and_path()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var shell = new ShellViewModel(new VaultManager(store, Path), Light,
            vaultPath: @"C:\v\vault.dat", appVersion: "9.9.9");
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);

        shell.ShowInfoCommand.Execute(null);

        var info = Assert.IsType<InfoViewModel>(shell.CurrentViewModel);
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal(@"C:\v\vault.dat", info.VaultPath);
    }

    [Fact]
    public async Task Show_items_returns_to_same_main()
    {
        var (shell, main) = await OpenedShellAsync();
        shell.ShowSettingsCommand.Execute(null);

        shell.ShowItemsCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);
        Assert.Equal(ShellSection.Items, shell.Section);
    }

    [Fact]
    public void Locked_state_hides_sidebar()
    {
        var shell = new ShellViewModel(new VaultManager(new InMemoryStore(), Path), Light);

        Assert.Null(shell.Section);
        Assert.False(shell.IsSidebarVisible);
    }

    [Fact]
    public async Task Subflow_hides_sidebar()
    {
        var (shell, main) = await OpenedShellAsync();

        main.NewEntryCommand.Execute(null); // 편집(집중 작업) 진입 → 사이드바 숨김

        Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        Assert.Null(shell.Section);
        Assert.False(shell.IsSidebarVisible);
    }

    [Fact]
    public async Task Shell_lock_returns_to_unlock_flow()
    {
        var (shell, _) = await OpenedShellAsync();

        shell.LockCommand.Execute(null);

        Assert.Equal(ShellState.Unlocking, shell.State);
        Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        Assert.False(shell.IsSidebarVisible);
    }

    // --- 슬랙 알림 이벤트 배선 (M6 S4, design 7.8) ---
    // 게이트(전역·활성·토글)는 SlackNotifierTests가 검증하므로, 여기선 셸이 올바른 이벤트를 "부르는지"만 본다.

    private sealed class FakeNotifier : PasswordManager.Core.Notifications.ISlackNotifier
    {
        public List<PasswordManager.Core.Notifications.SlackEvent> Events { get; } = new();
        public List<string?> SiteNames { get; } = new();

        public Task NotifyAsync(PasswordManager.Core.Notifications.SlackEvent kind, DateTimeOffset time,
            string? siteName = null, CancellationToken ct = default)
        {
            Events.Add(kind);
            SiteNames.Add(siteName);
            return Task.CompletedTask;
        }
    }

    private static ShellViewModel ShellWithNotifier(InMemoryStore store, FakeNotifier notifier)
        => new(new VaultManager(store, Path), Light,
               slack: notifier, slackCache: new PasswordManager.Core.Notifications.SlackConfigCache());

    [Fact]
    public async Task Successful_unlock_notifies_slack_unlock()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var notifier = new FakeNotifier();
        var shell = ShellWithNotifier(store, notifier);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);

        Assert.Contains(PasswordManager.Core.Notifications.SlackEvent.Unlock, notifier.Events);
    }

    [Fact]
    public async Task Wrong_password_notifies_slack_login_failure()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var notifier = new FakeNotifier();
        var shell = ShellWithNotifier(store, notifier);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = "wrong";
        await unlock.UnlockCommand.ExecuteAsync(null);

        Assert.Contains(PasswordManager.Core.Notifications.SlackEvent.LoginFailure, notifier.Events);
    }

    [Fact]
    public async Task Saving_entry_with_new_password_notifies_slack_password_change_with_title()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        var notifier = new FakeNotifier();
        var shell = ShellWithNotifier(store, notifier);

        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);
        unlock.Password = Master;
        await unlock.UnlockCommand.ExecuteAsync(null);
        var main = Assert.IsType<MainViewModel>(shell.CurrentViewModel);

        main.NewEntryCommand.Execute(null);
        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        editor.Title = "Steam";
        editor.Login = "gamer";
        editor.Password = "pw";
        editor.SaveCommand.Execute(null);

        Assert.Contains(PasswordManager.Core.Notifications.SlackEvent.PasswordChange, notifier.Events);
        Assert.Contains("Steam", notifier.SiteNames);
    }

    // --- 전체 초기화 (TD-044) ---

    private sealed class FakeEraser : PasswordManager.ViewModels.Services.IFileEraser
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Deleted { get; } = new();
        public bool Exists(string path) => Files.Contains(path);
        public void Delete(string path) { Files.Remove(path); Deleted.Add(path); }
    }

    private sealed class FakeDialog : PasswordManager.ViewModels.Services.IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCount { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }
        public void Notify(string title, string message) { }
    }

    /// <summary>언락 화면이 뜬 셸과, 볼트 사이드카가 전부 존재하는 가짜 파일 시스템을 만든다.</summary>
    private static (ShellViewModel shell, FakeEraser eraser, FakeDialog dialog) ShellAtUnlock()
    {
        const string vaultPath = @"C:\data\vault.dat";
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);

        var eraser = new FakeEraser();
        foreach (var p in VaultReset.PathsFor(vaultPath)) eraser.Files.Add(p);

        var dialog = new FakeDialog();
        var shell = new ShellViewModel(new VaultManager(store, Path), Light, vaultPath: vaultPath)
        {
            FileEraser = eraser,
            Dialog = dialog,
        };
        return (shell, eraser, dialog);
    }

    [Fact]
    public async Task Reset_command_confirmed_erases_every_file_and_returns_to_create_flow()
    {
        var (shell, eraser, dialog) = ShellAtUnlock();
        dialog.ConfirmResult = true;
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);

        unlock.Password = UnlockViewModel.ResetCommand;
        await unlock.UnlockCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Empty(eraser.Files);                       // 볼트·백업·잠금상태·로그 전부 삭제
        Assert.Equal(5, eraser.Deleted.Count);
        Assert.Equal(ShellState.Creating, shell.State);    // 새 볼트 만들기로 이동
        Assert.IsType<CreateVaultViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Reset_command_cancelled_keeps_every_file_and_stays_on_unlock()
    {
        var (shell, eraser, dialog) = ShellAtUnlock();
        dialog.ConfirmResult = false;
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);

        unlock.Password = UnlockViewModel.ResetCommand;
        await unlock.UnlockCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Empty(eraser.Deleted);                   // 아무것도 지우지 않았다
        Assert.Equal(5, eraser.Files.Count);
        Assert.Equal(ShellState.Unlocking, shell.State);
    }

    [Fact]
    public async Task Reset_skips_files_that_are_not_there()
    {
        var (shell, eraser, _) = ShellAtUnlock();
        eraser.Files.Remove(@"C:\data\vault.dat.bak");   // 백업이 아직 없는 새 볼트 상황
        eraser.Files.Remove(@"C:\data\slack-failures.log");
        var unlock = Assert.IsType<UnlockViewModel>(shell.CurrentViewModel);

        unlock.Password = UnlockViewModel.ResetCommand;
        await unlock.UnlockCommand.ExecuteAsync(null);

        Assert.Equal(3, eraser.Deleted.Count);
        Assert.Empty(eraser.Files);
    }
}
