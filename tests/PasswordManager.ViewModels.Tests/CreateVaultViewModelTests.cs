using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;
using PasswordManager.ViewModels.Services;

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

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
        public void Clear() => Text = null;
    }

    /// <summary>예약된 자동 삭제를 테스트가 직접 터뜨릴 수 있게 붙잡아 두는 스케줄러.</summary>
    private sealed class ManualScheduler : IScheduler
    {
        private Action? _pending;
        public bool HasPending => _pending is not null;
        public void Schedule(TimeSpan delay, Action action) => _pending = action;
        public void RunPending() { var a = _pending; _pending = null; a?.Invoke(); }
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

    [Fact]
    public async Task Acknowledge_is_gated_until_created_and_saved_is_checked()
    {
        var (vm, _) = NewVm();
        Assert.False(vm.AcknowledgeCommand.CanExecute(null)); // 생성 전

        vm.Password = "master-pass";
        vm.ConfirmPassword = "master-pass";
        await vm.CreateCommand.ExecuteAsync(null);
        Assert.False(vm.AcknowledgeCommand.CanExecute(null)); // 생성됐지만 확인 체크 전

        vm.RecoveryKeySaved = true;
        Assert.True(vm.AcknowledgeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Acknowledge_raises_Completed()
    {
        var (vm, _) = NewVm();
        vm.Password = "master-pass";
        vm.ConfirmPassword = "master-pass";
        await vm.CreateCommand.ExecuteAsync(null);
        vm.RecoveryKeySaved = true;

        var completed = false;
        vm.Completed += (_, _) => completed = true;
        vm.AcknowledgeCommand.Execute(null);

        Assert.True(completed);
    }

    // --- 복구 키 복사 (TD-043) ---

    [Fact]
    public async Task CopyRecoveryKey_puts_it_on_the_clipboard_and_schedules_auto_clear()
    {
        var clip = new FakeClipboard();
        var scheduler = new ManualScheduler();
        var manager = new VaultManager(new InMemoryStore(), Path);
        var vm = new CreateVaultViewModel(manager, Light, new ClipboardCopier(clip, scheduler));
        vm.Password = "correct horse battery staple";
        vm.ConfirmPassword = "correct horse battery staple";
        await vm.CreateCommand.ExecuteAsync(null);

        vm.CopyRecoveryKeyCommand.Execute(null);

        Assert.Equal(vm.RecoveryKeyDisplay, clip.Text);
        Assert.True(scheduler.HasPending); // 복구 키는 비밀 → 자동 삭제가 걸려야 한다

        scheduler.RunPending();
        Assert.Null(clip.Text);
    }

    [Fact]
    public void CopyRecoveryKey_disabled_until_a_key_exists()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        var vm = new CreateVaultViewModel(manager, Light,
            new ClipboardCopier(new FakeClipboard(), new ManualScheduler()));

        Assert.False(vm.CopyRecoveryKeyCommand.CanExecute(null));
    }
}
