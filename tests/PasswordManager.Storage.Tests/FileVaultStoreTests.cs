using System.Security.Cryptography;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.Storage;

namespace PasswordManager.Storage.Tests;

public class FileVaultStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileVaultStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pwm-fvs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "vault.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static byte[] R(int n) => RandomNumberGenerator.GetBytes(n);

    private static EncryptedVault SampleVault()
    {
        var header = new VaultHeader(
            Salt: R(16),
            Kdf: new KdfParams(MemoryKiB: 65536, Iterations: 3, Parallelism: 4),
            DekByMaster: new WrappedKey(R(12), R(32), R(16)),
            DekByRecovery: new WrappedKey(R(12), R(32), R(16)));
        return new EncryptedVault(header, R(12), R(128), R(16));
    }

    [Fact]
    public void Exists_is_false_before_save_true_after()
    {
        IVaultFileStore store = new FileVaultStore();

        Assert.False(store.Exists(_path));
        store.Save(_path, SampleVault());
        Assert.True(store.Exists(_path));
    }

    [Fact]
    public void Save_then_Load_roundtrips_through_disk()
    {
        IVaultFileStore store = new FileVaultStore();
        var vault = SampleVault();

        store.Save(_path, vault);
        var loaded = store.Load(_path);

        Assert.Equal(vault.Ciphertext, loaded.Ciphertext);
        Assert.Equal(vault.Tag, loaded.Tag);
        Assert.Equal(vault.Header.Salt, loaded.Header.Salt);
    }
}
