using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class CreateVaultViewModelTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Path = "vault.dat";

    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) => _files[path] = vault;
        public EncryptedVault Load(string path) => _files[path];
    }

    private static (CreateVaultViewModel vm, VaultManager manager) NewVm()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        return (new CreateVaultViewModel(manager, Light), manager);
    }

    [Fact]
    public void CreateCommand_disabled_until_both_fields_filled()
    {
        var (vm, _) = NewVm();
        Assert.False(vm.CreateCommand.CanExecute(null));

        vm.Password = "master-pass";
        Assert.False(vm.CreateCommand.CanExecute(null));

        vm.ConfirmPassword = "master-pass";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Create_with_mismatched_confirmation_sets_error_and_does_not_create()
    {
        var (vm, manager) = NewVm();
        vm.Password = "master-pass";
        vm.ConfirmPassword = "different";
        var raised = false;
        vm.Created += (_, _) => raised = true;

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(manager.IsUnlocked);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Null(vm.RecoveryKeyDisplay);
    }

    [Fact]
    public async Task Create_success_opens_session_shows_recovery_key_and_raises_Created()
    {
        var (vm, manager) = NewVm();
        vm.Password = "master-pass";
        vm.ConfirmPassword = "master-pass";
        var raised = false;
        vm.Created += (_, _) => raised = true;

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.True(manager.IsUnlocked);
        Assert.Null(vm.ErrorMessage);
        Assert.False(string.IsNullOrEmpty(vm.RecoveryKeyDisplay));
    }

    [Fact]
    public async Task Displayed_recovery_key_decodes_to_a_32_byte_key()
    {
        var (vm, _) = NewVm();
        vm.Password = "master-pass";
        vm.ConfirmPassword = "master-pass";

        await vm.CreateCommand.ExecuteAsync(null);

        var decoded = RecoveryCode.Decode(vm.RecoveryKeyDisplay!);
        Assert.Equal(VaultService.RecoveryKeySizeBytes, decoded.Length);
    }
}
