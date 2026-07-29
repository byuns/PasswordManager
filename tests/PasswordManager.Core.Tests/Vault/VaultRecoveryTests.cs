using System.Text;
using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

public class VaultRecoveryTests
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

    // ── VaultService ──

    [Fact]
    public void Reset_with_recovery_enables_new_master_blocks_old_and_keeps_recovery()
    {
        var content = Encoding.UTF8.GetBytes("secret-body");
        var created = VaultService.Create("old-master", content, Light);

        var reset = VaultService.ResetMasterPasswordWithRecovery(
            created.Vault, created.RecoveryKey, "new-master", Light);

        Assert.Equal(content, VaultService.OpenWithMaster(reset, "new-master"));
        Assert.Throws<InvalidMasterPasswordException>(() => VaultService.OpenWithMaster(reset, "old-master"));
        Assert.Equal(content, VaultService.OpenWithRecoveryKey(reset, created.RecoveryKey)); // 복구 래핑 유지
    }

    [Fact]
    public void Reset_with_wrong_recovery_key_throws()
    {
        var created = VaultService.Create("m", Encoding.UTF8.GetBytes("x"), Light);
        var wrong = new byte[VaultService.RecoveryKeySizeBytes];

        Assert.Throws<InvalidRecoveryKeyException>(() =>
            VaultService.ResetMasterPasswordWithRecovery(created.Vault, wrong, "new", Light));
    }

    // ── VaultManager ──

    [Fact]
    public void Recover_resets_master_and_opens_session_with_content()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        var recoveryKey = m1.CreateNew("old-master", Light);
        m1.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var code = RecoveryCode.Encode(recoveryKey);

        var m2 = new VaultManager(store, Path);
        m2.Recover(code, "new-master", Light);

        Assert.True(m2.IsUnlocked);
        Assert.Single(m2.Entries);

        new VaultManager(store, Path).Open("new-master"); // 새 비번으로 열림(예외 없음)
        Assert.Throws<InvalidMasterPasswordException>(() => new VaultManager(store, Path).Open("old-master"));
    }

    [Fact]
    public void Recover_with_wrong_code_throws()
    {
        var store = new InMemoryStore();
        new VaultManager(store, Path).CreateNew("old-master", Light);
        var wrongCode = RecoveryCode.Encode(new byte[VaultService.RecoveryKeySizeBytes]);

        var m = new VaultManager(store, Path);
        Assert.Throws<InvalidRecoveryKeyException>(() => m.Recover(wrongCode, "new-master", Light));
    }
}
