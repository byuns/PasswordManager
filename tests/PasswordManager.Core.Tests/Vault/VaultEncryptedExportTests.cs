using System.Text;
using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

/// <summary>복구 키로 잠근 내보내기·가져오기(TD-050)의 매니저 레벨 동작.</summary>
public class VaultEncryptedExportTests
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

    /// <summary>언락된 볼트와 그 복구 키 문자열을 함께 돌려준다.</summary>
    private static (VaultManager Manager, string RecoveryCode) NewVault()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        var key = manager.CreateNew(Master, Light);
        return (manager, RecoveryCode.Encode(key));
    }

    private static VaultEntry NewEntry(string title = "Steam") => new()
    {
        Title = title,
        Url = "https://store.steampowered.com",
        Login = "gamer@example.com",
        Password = "s3cr3t",
        Tags = { "게임" },
    };

    [Fact]
    public void VerifyRecoveryKey_accepts_the_vaults_own_key()
    {
        var (manager, code) = NewVault();

        Assert.True(manager.VerifyRecoveryKey(code));
    }

    [Fact]
    public void VerifyRecoveryKey_rejects_another_vaults_key()
    {
        var (manager, _) = NewVault();
        var (_, otherCode) = NewVault();

        Assert.False(manager.VerifyRecoveryKey(otherCode));
    }

    [Fact]
    public void VerifyRecoveryKey_rejects_malformed_code()
    {
        var (manager, _) = NewVault();

        Assert.False(manager.VerifyRecoveryKey("not-a-key!!"));  // 알파벳 밖 문자
        Assert.False(manager.VerifyRecoveryKey("ABCD-EFGH"));    // 길이 부족
        Assert.False(manager.VerifyRecoveryKey(""));
    }

    [Fact]
    public void ExportEncrypted_rejects_a_key_that_is_not_this_vaults()
    {
        // 틀린 키로 잠그면 그 파일은 영영 못 연다 — 봉인 전에 반드시 막아야 한다.
        var (manager, _) = NewVault();
        manager.Add(NewEntry());
        var (_, otherCode) = NewVault();

        Assert.Throws<InvalidRecoveryKeyException>(() => manager.ExportEncrypted(otherCode));
    }

    [Fact]
    public void ExportEncrypted_output_keeps_no_plaintext()
    {
        var (manager, code) = NewVault();
        manager.Add(NewEntry());

        var file = manager.ExportEncrypted(code);

        var text = Encoding.UTF8.GetString(file);
        Assert.DoesNotContain("s3cr3t", text);
        Assert.DoesNotContain("gamer@example.com", text);
        Assert.DoesNotContain("Steam", text);
    }

    [Fact]
    public void ExportEncrypted_then_ImportEncrypted_roundtrips_into_another_vault()
    {
        var (source, code) = NewVault();
        source.Add(NewEntry("Steam"));
        source.Add(NewEntry("GitHub"));
        var file = source.ExportEncrypted(code);

        // 받는 쪽은 다른 볼트(다른 마스터 비번·다른 복구 키)여도 된다 — 파일의 복구 키로만 열린다.
        var (target, _) = NewVault();
        var count = target.ImportEncrypted(file, code);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "GitHub", "Steam" }, target.Entries.Select(e => e.Title).OrderBy(t => t));
        var steam = target.Entries.First(e => e.Title == "Steam");
        Assert.Equal("s3cr3t", steam.Password);
        Assert.Equal("https://store.steampowered.com", steam.Url);
        Assert.Equal(new[] { "게임" }, steam.Tags);
    }

    [Fact]
    public void ImportEncrypted_with_wrong_key_throws_and_adds_nothing()
    {
        var (source, code) = NewVault();
        source.Add(NewEntry());
        var file = source.ExportEncrypted(code);

        var (target, targetCode) = NewVault();

        Assert.Throws<InvalidRecoveryKeyException>(() => target.ImportEncrypted(file, targetCode));
        Assert.Empty(target.Entries);
    }

    [Fact]
    public void ImportEncrypted_rejects_a_plain_csv_file()
    {
        var (manager, code) = NewVault();
        var plain = Encoding.UTF8.GetBytes(CsvVault.Export(new[] { NewEntry() }));

        Assert.Throws<FormatException>(() => manager.ImportEncrypted(plain, code));
    }

    [Fact]
    public void ExportEncrypted_excludes_trashed_entries()
    {
        var (manager, code) = NewVault();
        manager.Add(NewEntry("Steam"));
        manager.Add(NewEntry("GitHub"));
        manager.Remove(manager.Entries.First(e => e.Title == "GitHub").Id);

        var file = manager.ExportEncrypted(code);
        var (target, _) = NewVault();
        target.ImportEncrypted(file, code);

        Assert.Equal("Steam", Assert.Single(target.Entries).Title);
    }
}
