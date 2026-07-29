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
}
