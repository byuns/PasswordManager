using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class ChangeMasterPasswordViewModelTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string OldMaster = "old-master";
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
        m.CreateNew(OldMaster, Light);
        return m;
    }

    [Fact]
    public void Command_disabled_until_all_fields_filled()
    {
        var vm = new ChangeMasterPasswordViewModel(Unlocked(), Light);
        Assert.False(vm.ChangeCommand.CanExecute(null));

        vm.CurrentPassword = OldMaster;
        vm.NewPassword = "new-master";
        Assert.False(vm.ChangeCommand.CanExecute(null));

        vm.ConfirmPassword = "new-master";
        Assert.True(vm.ChangeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Mismatched_confirmation_sets_error_and_does_not_change()
    {
        var manager = Unlocked();
        var vm = new ChangeMasterPasswordViewModel(manager, Light)
        {
            CurrentPassword = OldMaster,
            NewPassword = "new-master",
            ConfirmPassword = "different",
        };
        var raised = false;
        vm.Changed += (_, _) => raised = true;

        await vm.ChangeCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Wrong_current_password_sets_error()
    {
        var vm = new ChangeMasterPasswordViewModel(Unlocked(), Light)
        {
            CurrentPassword = "wrong",
            NewPassword = "new-master",
            ConfirmPassword = "new-master",
        };
        var raised = false;
        vm.Changed += (_, _) => raised = true;

        await vm.ChangeCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Successful_change_raises_Changed()
    {
        var vm = new ChangeMasterPasswordViewModel(Unlocked(), Light)
        {
            CurrentPassword = OldMaster,
            NewPassword = "new-master",
            ConfirmPassword = "new-master",
        };
        var raised = false;
        vm.Changed += (_, _) => raised = true;

        await vm.ChangeCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Cancel_raises_Cancelled()
    {
        var vm = new ChangeMasterPasswordViewModel(Unlocked(), Light);
        var cancelled = false;
        vm.Cancelled += (_, _) => cancelled = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}
