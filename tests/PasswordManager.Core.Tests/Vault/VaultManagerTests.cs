using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

public class VaultManagerTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Master = "correct horse battery staple";
    private const string Path = "vault.dat";

    /// <summary>디스크 없이 저장/로드를 검증하기 위한 인메모리 IVaultFileStore.</summary>
    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public int SaveCount { get; private set; }
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) { _files[path] = vault; SaveCount++; }
        public EncryptedVault Load(string path) => _files[path];
    }

    private static VaultEntry NewEntry(string title = "Steam") => new()
    {
        Title = title,
        Login = "gamer@example.com",
        Password = "s3cr3t",
    };

    [Fact]
    public void CreateNew_persists_empty_vault_and_returns_recovery_key()
    {
        var store = new InMemoryStore();
        var manager = new VaultManager(store, Path);

        var recoveryKey = manager.CreateNew(Master, Light);

        Assert.True(store.Exists(Path));
        Assert.Equal(VaultService.RecoveryKeySizeBytes, recoveryKey.Length);
        Assert.Empty(manager.Entries);
        Assert.True(manager.IsUnlocked);
    }

    [Fact]
    public void Add_then_reopen_with_new_manager_sees_entry()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        m1.CreateNew(Master, Light);
        m1.Add(NewEntry("Steam"));

        var m2 = new VaultManager(store, Path);
        m2.Open(Master);

        var e = Assert.Single(m2.Entries);
        Assert.Equal("Steam", e.Title);
        Assert.Equal("gamer@example.com", e.Login);
        Assert.Equal("s3cr3t", e.Password);
    }

    [Fact]
    public void Add_stamps_timestamps()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        m.Add(NewEntry());

        var e = Assert.Single(m.Entries);
        Assert.True(e.CreatedAt >= before);
        Assert.True(e.UpdatedAt >= before);
        Assert.True(e.LastChangedAt >= before);
    }

    [Fact]
    public void Update_persists_changed_fields()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        m1.CreateNew(Master, Light);
        var entry = NewEntry("Steam");
        m1.Add(entry);

        entry.Title = "Steam (main)";
        entry.Notes = "changed";
        m1.Update(entry);

        var m2 = new VaultManager(store, Path);
        m2.Open(Master);
        var e = Assert.Single(m2.Entries);
        Assert.Equal("Steam (main)", e.Title);
        Assert.Equal("changed", e.Notes);
    }

    [Fact]
    public void Remove_persists_deletion()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        m1.CreateNew(Master, Light);
        var entry = NewEntry();
        m1.Add(entry);

        m1.Remove(entry.Id);

        var m2 = new VaultManager(store, Path);
        m2.Open(Master);
        Assert.Empty(m2.Entries);
    }

    [Fact]
    public void Open_with_wrong_password_throws()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        m1.CreateNew(Master, Light);

        var m2 = new VaultManager(store, Path);
        Assert.Throws<InvalidMasterPasswordException>(() => m2.Open("wrong-password"));
    }

    [Fact]
    public void Add_before_unlock_throws()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);

        Assert.Throws<InvalidOperationException>(() => m.Add(NewEntry()));
    }

    [Fact]
    public void Lock_clears_session_and_blocks_crud()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        m.Lock();

        Assert.False(m.IsUnlocked);
        Assert.Throws<InvalidOperationException>(() => m.Add(NewEntry()));
    }

    // --- 앱 잠금해제 OTP 게이트 (design 5.4, TD-004) ---

    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);

    [Fact]
    public void Otp_is_not_registered_on_new_vault()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        Assert.False(m.HasOtp);
    }

    [Fact]
    public void SetupOtp_registers_and_returns_verifiable_secret()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        var secret = m.SetupOtp();
        var code = TotpValidator.GenerateCode(secret, FixedNow);

        Assert.True(m.HasOtp);
        Assert.True(m.VerifyOtp(code, FixedNow));
    }

    [Fact]
    public void SetupOtp_secret_persists_across_reopen()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        m1.CreateNew(Master, Light);
        var secret = m1.SetupOtp();

        var m2 = new VaultManager(store, Path);
        m2.Open(Master);

        Assert.True(m2.HasOtp);
        Assert.True(m2.VerifyOtp(TotpValidator.GenerateCode(secret, FixedNow), FixedNow));
    }

    [Fact]
    public void VerifyOtp_rejects_wrong_code()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);
        m.SetupOtp();

        Assert.False(m.VerifyOtp("000000", FixedNow));
    }

    [Fact]
    public void VerifyOtp_before_setup_throws()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        Assert.Throws<InvalidOperationException>(() => m.VerifyOtp("123456", FixedNow));
    }

    [Fact]
    public void SetupOtp_before_unlock_throws()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);

        Assert.Throws<InvalidOperationException>(() => m.SetupOtp());
    }

    [Fact]
    public void SetupOtp_called_again_rotates_secret()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        var first = m.SetupOtp();
        var second = m.SetupOtp();

        Assert.NotEqual(first, second);
        // 재설정 후 예전 secret 기준 코드는 더 이상 통과하지 않는다(TD-005).
        Assert.False(m.VerifyOtp(TotpValidator.GenerateCode(first, FixedNow), FixedNow));
        Assert.True(m.VerifyOtp(TotpValidator.GenerateCode(second, FixedNow), FixedNow));
    }
}
