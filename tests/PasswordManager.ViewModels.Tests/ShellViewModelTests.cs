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
}
