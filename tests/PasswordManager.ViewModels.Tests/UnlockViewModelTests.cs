using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class UnlockViewModelTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Master = "correct horse battery staple";
    private const string Path = "vault.dat";

    /// <summary>디스크 없이 저장/로드를 검증하기 위한 인메모리 IVaultFileStore.</summary>
    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) => _files[path] = vault;
        public EncryptedVault Load(string path) => _files[path];
    }

    /// <summary>기존 볼트가 저장된 스토어와, 아직 잠긴 새 매니저를 만든다.</summary>
    private static VaultManager ManagerWithExistingVault()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        return new VaultManager(store, Path); // 잠긴 상태로 반환
    }

    [Fact]
    public void UnlockCommand_disabled_when_password_empty()
    {
        var vm = new UnlockViewModel(ManagerWithExistingVault());

        Assert.False(vm.UnlockCommand.CanExecute(null));

        vm.Password = "something";
        Assert.True(vm.UnlockCommand.CanExecute(null));
    }

    [Fact]
    public async Task Unlock_with_correct_password_raises_Unlocked_and_opens_session()
    {
        var manager = ManagerWithExistingVault();
        var vm = new UnlockViewModel(manager) { Password = Master };
        var raised = false;
        vm.Unlocked += (_, _) => raised = true;

        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.True(manager.IsUnlocked);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Unlock_with_wrong_password_sets_error_and_stays_locked()
    {
        var manager = ManagerWithExistingVault();
        var vm = new UnlockViewModel(manager) { Password = "wrong-password" };
        var raised = false;
        vm.Unlocked += (_, _) => raised = true;

        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.False(manager.IsUnlocked);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Unlock_clears_previous_error_on_retry()
    {
        var manager = ManagerWithExistingVault();
        var vm = new UnlockViewModel(manager) { Password = "wrong-password" };
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));

        vm.Password = Master;
        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.True(manager.IsUnlocked);
    }
}
