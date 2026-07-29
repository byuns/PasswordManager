using System.Security.Cryptography;
using System.Text;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.Storage;

namespace PasswordManager.Storage.Tests;

public class VaultFileStoreTests : IDisposable
{
    private readonly string _dir;

    public VaultFileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pwm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static byte[] R(int n) => RandomNumberGenerator.GetBytes(n);

    private static EncryptedVault SampleVault(byte[]? body = null)
    {
        var header = new VaultHeader(
            Salt: R(16),
            Kdf: new KdfParams(MemoryKiB: 65536, Iterations: 3, Parallelism: 4),
            DekByMaster: new WrappedKey(R(12), R(32), R(16)),
            DekByRecovery: new WrappedKey(R(12), R(32), R(16)));
        return new EncryptedVault(header, R(12), body ?? R(128), R(16));
    }

    private static void AssertWrappedEqual(WrappedKey a, WrappedKey b)
    {
        Assert.Equal(a.Nonce, b.Nonce);
        Assert.Equal(a.Ciphertext, b.Ciphertext);
        Assert.Equal(a.Tag, b.Tag);
    }

    private static void AssertVaultEqual(EncryptedVault a, EncryptedVault b)
    {
        Assert.Equal(a.Header.Salt, b.Header.Salt);
        Assert.Equal(a.Header.Kdf, b.Header.Kdf);
        AssertWrappedEqual(a.Header.DekByMaster, b.Header.DekByMaster);
        AssertWrappedEqual(a.Header.DekByRecovery, b.Header.DekByRecovery);
        Assert.Equal(a.Nonce, b.Nonce);
        Assert.Equal(a.Ciphertext, b.Ciphertext);
        Assert.Equal(a.Tag, b.Tag);
    }

    [Fact]
    public void Serialize_then_Deserialize_roundtrips_vault()
    {
        var vault = SampleVault();

        var restored = VaultFileStore.Deserialize(VaultFileStore.Serialize(vault));

        AssertVaultEqual(vault, restored);
    }

    [Fact]
    public void Serialized_bytes_start_with_magic_and_version()
    {
        var bytes = VaultFileStore.Serialize(SampleVault());

        Assert.Equal(Encoding.ASCII.GetBytes(VaultFileStore.Magic), bytes[..4]);
        Assert.Equal(VaultFileStore.CurrentVersion, bytes[4]);
    }

    [Fact]
    public void Deserialize_with_wrong_magic_throws()
    {
        var bytes = VaultFileStore.Serialize(SampleVault());
        bytes[0] ^= 0xFF; // 매직 손상

        Assert.Throws<InvalidVaultFileException>(() => VaultFileStore.Deserialize(bytes));
    }

    [Fact]
    public void Deserialize_with_unsupported_version_throws()
    {
        var bytes = VaultFileStore.Serialize(SampleVault());
        bytes[4] = 0xFF; // 지원하지 않는 버전

        Assert.Throws<InvalidVaultFileException>(() => VaultFileStore.Deserialize(bytes));
    }

    [Fact]
    public void Save_then_Load_roundtrips_vault()
    {
        var path = Path.Combine(_dir, "vault.dat");
        var vault = SampleVault();

        VaultFileStore.Save(path, vault);
        var loaded = VaultFileStore.Load(path);

        AssertVaultEqual(vault, loaded);
    }

    [Fact]
    public void Save_leaves_no_tmp_file()
    {
        var path = Path.Combine(_dir, "vault.dat");

        VaultFileStore.Save(path, SampleVault());

        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_over_existing_file_preserves_previous_content_as_bak()
    {
        var path = Path.Combine(_dir, "vault.dat");
        var first = SampleVault(body: Encoding.UTF8.GetBytes("first-version-body"));
        var second = SampleVault(body: Encoding.UTF8.GetBytes("second-version-body"));

        VaultFileStore.Save(path, first);
        VaultFileStore.Save(path, second);

        // .bak에는 직전(first) 내용이 보존되어 있어야 한다.
        AssertVaultEqual(first, VaultFileStore.Load(path + ".bak"));
        AssertVaultEqual(second, VaultFileStore.Load(path));
    }

    [Fact]
    public void Load_missing_file_throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => VaultFileStore.Load(Path.Combine(_dir, "nope.dat")));
    }
}
