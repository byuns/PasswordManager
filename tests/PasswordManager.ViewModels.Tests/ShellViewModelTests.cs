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
    public async Task Edit_request_opens_prefilled_editor()
    {
        var (shell, main) = await OpenedShellAsync();
        main.NewEntryCommand.Execute(null);
        var adder = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        adder.Title = "Steam";
        adder.SaveCommand.Execute(null);

        main.SelectedEntry = main.Entries[0];
        main.EditCommand.Execute(null);

        var editor = Assert.IsType<EntryEditViewModel>(shell.CurrentViewModel);
        Assert.False(editor.IsNew);
        Assert.Equal("Steam", editor.Title);
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

    [Fact]
    public async Task Change_master_request_opens_editor_and_success_returns_to_main()
    {
        var (shell, main) = await OpenedShellAsync();

        main.ChangeMasterPasswordCommand.Execute(null);
        var change = Assert.IsType<ChangeMasterPasswordViewModel>(shell.CurrentViewModel);
        change.CurrentPassword = Master;
        change.NewPassword = "new-master";
        change.ConfirmPassword = "new-master";
        await change.ChangeCommand.ExecuteAsync(null);

        Assert.Same(main, shell.CurrentViewModel);
        Assert.Equal(ShellState.Open, shell.State);
    }

    [Fact]
    public async Task Cancel_change_master_returns_to_main()
    {
        var (shell, main) = await OpenedShellAsync();

        main.ChangeMasterPasswordCommand.Execute(null);
        var change = Assert.IsType<ChangeMasterPasswordViewModel>(shell.CurrentViewModel);
        change.CancelCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);
    }

    // --- OTP 등록 마법사 / 열람 게이트 내비게이션 (design 5.4·7.4) ---

    /// <summary>셸을 열고 OTP 등록 마법사를 통해 OTP를 등록한 뒤 메인으로 복귀한다.</summary>
    private static async Task<(ShellViewModel shell, MainViewModel main)> OpenedShellWithOtpAsync()
    {
        var (shell, main) = await OpenedShellAsync();
        main.SetupOtpCommand.Execute(null);
        var wizard = Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);
        wizard.VerificationCode = TotpValidator.GenerateCode(wizard.Secret, DateTimeOffset.UtcNow);
        wizard.ConfirmCommand.Execute(null);
        return (shell, main);
    }

    [Fact]
    public async Task Otp_setup_request_opens_wizard()
    {
        var (shell, main) = await OpenedShellAsync();

        main.SetupOtpCommand.Execute(null);

        Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Otp_setup_complete_returns_to_main_and_marks_registered()
    {
        var (shell, main) = await OpenedShellWithOtpAsync();

        Assert.Same(main, shell.CurrentViewModel);
        Assert.True(main.IsOtpRegistered);
    }

    [Fact]
    public async Task Cancel_otp_setup_returns_to_main()
    {
        var (shell, main) = await OpenedShellAsync();
        main.SetupOtpCommand.Execute(null);
        var wizard = Assert.IsType<OtpSetupViewModel>(shell.CurrentViewModel);

        wizard.CancelCommand.Execute(null);

        Assert.Same(main, shell.CurrentViewModel);
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
}
