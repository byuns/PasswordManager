using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

public class VaultBackupTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Master = "correct horse battery staple";
    private const string VaultPath = "vault.dat";
    private const string BackupPath = "backup.dat";

    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) => _files[path] = vault;
        public EncryptedVault Load(string path) => _files[path];
    }

    [Fact]
    public void Backup_copies_encrypted_vault_openable_with_same_master()
    {
        var store = new InMemoryStore();
        new VaultManager(store, VaultPath, Light).CreateNew(Master, Light);

        VaultBackup.Backup(store, VaultPath, BackupPath);

        Assert.True(store.Exists(BackupPath));
        // 백업은 암호문 그대로 → 같은 마스터 비번으로 열린다(언락 불필요하게 복사됨).
        Assert.NotNull(VaultService.OpenWithMaster(store.Load(BackupPath), Master));
    }

    [Fact]
    public void Backup_throws_when_no_vault_to_back_up()
    {
        var store = new InMemoryStore();

        Assert.Throws<InvalidOperationException>(() => VaultBackup.Backup(store, VaultPath, BackupPath));
    }

    [Fact]
    public void Restore_replaces_current_vault_with_backup_snapshot()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, VaultPath, Light);
        m.CreateNew(Master, Light);
        m.Add(new VaultEntry { Title = "Original", Password = "pw" });
        VaultBackup.Backup(store, VaultPath, BackupPath);
        m.Add(new VaultEntry { Title = "Later", Password = "pw2" }); // 백업 이후 변경

        VaultBackup.Restore(store, BackupPath, VaultPath);

        var reopened = new VaultManager(store, VaultPath, Light);
        reopened.Open(Master);
        Assert.Equal("Original", Assert.Single(reopened.Entries).Title); // 백업 시점으로 되돌림
    }

    [Fact]
    public void Restore_throws_when_backup_missing()
    {
        var store = new InMemoryStore();
        new VaultManager(store, VaultPath, Light).CreateNew(Master, Light);

        Assert.Throws<InvalidOperationException>(() => VaultBackup.Restore(store, BackupPath, VaultPath));
    }
}
