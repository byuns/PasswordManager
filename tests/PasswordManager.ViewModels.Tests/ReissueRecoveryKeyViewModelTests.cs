using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class ReissueRecoveryKeyViewModelTests
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
    public void Reissue_disabled_until_current_password_entered()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light);
        Assert.False(vm.ReissueCommand.CanExecute(null));

        vm.CurrentPassword = Master;
        Assert.True(vm.ReissueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Wrong_current_password_sets_error_and_does_not_reveal_key()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light) { CurrentPassword = "wrong" };

        await vm.ReissueCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Null(vm.RecoveryKeyDisplay);
    }

    [Fact]
    public async Task Successful_reissue_reveals_new_key_and_clears_error()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light) { CurrentPassword = Master };

        await vm.ReissueCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.RecoveryKeyDisplay));
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Reissue_hidden_once_key_revealed()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light) { CurrentPassword = Master };
        await vm.ReissueCommand.ExecuteAsync(null);

        // 이미 발급했으면 다시 발급 명령은 비활성(1회성).
        Assert.False(vm.ReissueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Acknowledge_disabled_until_saved_checkbox_ticked()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light) { CurrentPassword = Master };
        await vm.ReissueCommand.ExecuteAsync(null);

        Assert.False(vm.AcknowledgeCommand.CanExecute(null)); // 키는 나왔지만 아직 체크 전

        vm.RecoveryKeySaved = true;
        Assert.True(vm.AcknowledgeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Acknowledge_raises_Completed()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light) { CurrentPassword = Master };
        await vm.ReissueCommand.ExecuteAsync(null);
        vm.RecoveryKeySaved = true;
        var completed = false;
        vm.Completed += (_, _) => completed = true;

        vm.AcknowledgeCommand.Execute(null);

        Assert.True(completed);
    }

    [Fact]
    public void Cancel_raises_Cancelled()
    {
        var vm = new ReissueRecoveryKeyViewModel(Unlocked(), Light);
        var cancelled = false;
        vm.Cancelled += (_, _) => cancelled = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}
