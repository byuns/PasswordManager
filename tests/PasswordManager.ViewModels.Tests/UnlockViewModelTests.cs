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

    private sealed class MemoryLockoutStore : ILockoutStore
    {
        private LockoutState _state = LockoutState.Empty;
        public LockoutState Load() => _state;
        public void Save(LockoutState state) => _state = state;
    }

    [Fact]
    public async Task Fifth_wrong_password_locks_out_and_blocks_further_attempts()
    {
        var manager = ManagerWithExistingVault();
        var throttle = new LoginThrottle(new MemoryLockoutStore()); // 실제 시각
        var vm = new UnlockViewModel(manager, throttle) { Password = "wrong-password" };

        for (var i = 0; i < 5; i++)
            await vm.UnlockCommand.ExecuteAsync(null);
        Assert.Contains("잠깁니다", vm.ErrorMessage); // 5회째 잠금 안내

        // 잠긴 동안에는 올바른 비번을 넣어도 열지 않고 남은 시간을 안내한다
        vm.Password = Master;
        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.False(manager.IsUnlocked);
        Assert.Contains("후 다시 시도", vm.ErrorMessage);
    }

    [Fact]
    public async Task Successful_unlock_resets_failure_count()
    {
        var manager = ManagerWithExistingVault();
        var store = new MemoryLockoutStore();
        var throttle = new LoginThrottle(store);
        var vm = new UnlockViewModel(manager, throttle) { Password = "wrong-password" };
        await vm.UnlockCommand.ExecuteAsync(null); // 1회 실패 기록

        vm.Password = Master;
        await vm.UnlockCommand.ExecuteAsync(null); // 성공

        Assert.True(manager.IsUnlocked);
        Assert.Equal(LockoutState.Empty, store.Load());
    }

    /// <summary>본문 인증 태그를 훼손해 "비번은 맞지만 파일 손상"(VaultCorruptedException) 상황을 만든다.</summary>
    private static byte[] FlipFirstByte(byte[] bytes)
    {
        var copy = (byte[])bytes.Clone();
        copy[0] ^= 0xFF;
        return copy;
    }

    [Fact]
    public async Task Unlock_with_corrupted_vault_reports_corruption_and_is_not_a_failed_attempt()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        // 비번은 맞지만 본문이 손상된 파일을 만든다(태그 훼손 → DEK는 풀리나 본문 인증 실패).
        var tampered = store.Load(Path) with { Tag = FlipFirstByte(store.Load(Path).Tag) };
        store.Save(Path, tampered);

        var manager = new VaultManager(store, Path);
        var lockoutStore = new MemoryLockoutStore();
        var throttle = new LoginThrottle(lockoutStore);
        var vm = new UnlockViewModel(manager, throttle) { Password = Master };

        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.False(manager.IsUnlocked);
        Assert.Contains("손상", vm.ErrorMessage);              // 비번 오류와 구분되는 안내
        Assert.Equal(LockoutState.Empty, lockoutStore.Load()); // 손상은 실패 시도로 세지 않음
    }

    [Fact]
    public async Task Corrupted_unlock_flags_IsCorrupted_for_restore_cta()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        store.Save(Path, store.Load(Path) with { Tag = FlipFirstByte(store.Load(Path).Tag) });
        var vm = new UnlockViewModel(new VaultManager(store, Path)) { Password = Master };

        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.True(vm.IsCorrupted);
    }

    [Fact]
    public async Task Wrong_password_does_not_flag_IsCorrupted()
    {
        var vm = new UnlockViewModel(ManagerWithExistingVault()) { Password = "wrong-password" };

        await vm.UnlockCommand.ExecuteAsync(null);

        Assert.False(vm.IsCorrupted);
    }

    [Fact]
    public void RestoreCommand_raises_RestoreRequested()
    {
        var vm = new UnlockViewModel(ManagerWithExistingVault());
        var raised = false;
        vm.RestoreRequested += (_, _) => raised = true;

        vm.RestoreCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public async Task PerformRestore_replaces_vault_so_it_unlocks_again()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew(Master, Light);
        const string Backup = "vault.dat.bak";
        store.Save(Backup, store.Load(Path)); // 정상 볼트를 백업 경로에 보관
        // 본문을 훼손해 손상 상태로 만든다
        store.Save(Path, store.Load(Path) with { Tag = FlipFirstByte(store.Load(Path).Tag) });

        var manager = new VaultManager(store, Path);
        var vm = new UnlockViewModel(manager) { Password = Master };
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.True(vm.IsCorrupted); // 손상 감지됨

        vm.PerformRestore(Backup); // 백업으로 복원

        Assert.False(vm.IsCorrupted);
        Assert.Null(vm.ErrorMessage);

        vm.Password = Master; // 복원된 파일은 백업 시점 마스터 비번으로 열린다
        await vm.UnlockCommand.ExecuteAsync(null);
        Assert.True(manager.IsUnlocked);
    }

    [Fact]
    public void ForgotPassword_raises_RecoveryRequested()
    {
        var vm = new UnlockViewModel(ManagerWithExistingVault());
        var raised = false;
        vm.RecoveryRequested += (_, _) => raised = true;

        vm.ForgotPasswordCommand.Execute(null);

        Assert.True(raised);
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
