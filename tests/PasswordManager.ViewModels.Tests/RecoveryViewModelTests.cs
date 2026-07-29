using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class RecoveryViewModelTests
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

    /// <summary>기존 볼트를 만들고, (잠긴 매니저, 유효한 복구 코드)를 돌려준다.</summary>
    private static (VaultManager manager, string code) Setup()
    {
        var store = new InMemoryStore();
        var recoveryKey = new VaultManager(store, Path).CreateNew(OldMaster, Light);
        return (new VaultManager(store, Path), RecoveryCode.Encode(recoveryKey));
    }

    [Fact]
    public void RecoverCommand_disabled_until_all_fields_filled()
    {
        var (manager, code) = Setup();
        var vm = new RecoveryViewModel(manager, Light);
        Assert.False(vm.RecoverCommand.CanExecute(null));

        vm.RecoveryCodeInput = code;
        vm.NewPassword = "new-master";
        Assert.False(vm.RecoverCommand.CanExecute(null));

        vm.ConfirmPassword = "new-master";
        Assert.True(vm.RecoverCommand.CanExecute(null));
    }

    [Fact]
    public async Task Mismatched_confirmation_sets_error_and_does_not_recover()
    {
        var (manager, code) = Setup();
        var vm = new RecoveryViewModel(manager, Light)
        {
            RecoveryCodeInput = code,
            NewPassword = "new-master",
            ConfirmPassword = "different",
        };
        var raised = false;
        vm.Recovered += (_, _) => raised = true;

        await vm.RecoverCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(manager.IsUnlocked);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Wrong_recovery_code_sets_error_and_stays_locked()
    {
        var (manager, _) = Setup();
        var wrongCode = RecoveryCode.Encode(new byte[VaultService.RecoveryKeySizeBytes]);
        var vm = new RecoveryViewModel(manager, Light)
        {
            RecoveryCodeInput = wrongCode,
            NewPassword = "new-master",
            ConfirmPassword = "new-master",
        };
        var raised = false;
        vm.Recovered += (_, _) => raised = true;

        await vm.RecoverCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(manager.IsUnlocked);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Successful_recovery_opens_session_and_raises_Recovered()
    {
        var (manager, code) = Setup();
        var vm = new RecoveryViewModel(manager, Light)
        {
            RecoveryCodeInput = code,
            NewPassword = "new-master",
            ConfirmPassword = "new-master",
        };
        var raised = false;
        vm.Recovered += (_, _) => raised = true;

        await vm.RecoverCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.True(manager.IsUnlocked);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Cancel_raises_Cancelled()
    {
        var (manager, _) = Setup();
        var vm = new RecoveryViewModel(manager, Light);
        var cancelled = false;
        vm.Cancelled += (_, _) => cancelled = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}
