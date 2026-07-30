using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

public class CsvVaultTests
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

    [Fact]
    public void Export_writes_header_and_rows()
    {
        var csv = CsvVault.Export(new[]
        {
            new VaultEntry { Title = "Steam", Url = "https://s", Login = "gamer", Password = "pw", Notes = "n", Tags = { "game", "main" } },
        });

        var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal("title,url,login,password,notes,tags", lines[0]);
        Assert.Equal("Steam,https://s,gamer,pw,n,game|main", lines[1]);
    }

    [Fact]
    public void Roundtrips_fields_needing_escaping()
    {
        var original = new VaultEntry
        {
            Title = "A, Inc",                 // 쉼표
            Url = "https://x",
            Login = "he said \"hi\"",         // 따옴표
            Password = "line1\nline2",         // 개행
            Notes = "plain",
            Tags = { "t1", "t2" },
        };

        var parsed = CsvVault.Parse(CsvVault.Export(new[] { original }));

        var e = Assert.Single(parsed);
        Assert.Equal("A, Inc", e.Title);
        Assert.Equal("he said \"hi\"", e.Login);
        Assert.Equal("line1\nline2", e.Password);
        Assert.Equal(new[] { "t1", "t2" }, e.Tags);
    }

    [Fact]
    public void Parse_skips_header_and_blank_lines()
    {
        var csv = "title,url,login,password,notes,tags\r\nSteam,,gamer,pw,,\r\n\r\n";

        var parsed = CsvVault.Parse(csv);

        var e = Assert.Single(parsed);
        Assert.Equal("Steam", e.Title);
        Assert.Equal("gamer", e.Login);
        Assert.Empty(e.Tags);
    }

    [Fact]
    public void Time_settings_default_to_5min_and_20sec()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        Assert.Equal(5, m.AutoLockMinutes);
        Assert.Equal(20, m.ClipboardClearSeconds);
    }

    [Fact]
    public void SetTimeSettings_persists_across_reopen()
    {
        var store = new InMemoryStore();
        var m1 = new VaultManager(store, Path);
        m1.CreateNew(Master, Light);

        m1.SetTimeSettings(autoLockMinutes: 10, clipboardClearSeconds: 30);

        var m2 = new VaultManager(store, Path);
        m2.Open(Master);
        Assert.Equal(10, m2.AutoLockMinutes);
        Assert.Equal(30, m2.ClipboardClearSeconds);
    }

    [Fact]
    public void SetTimeSettings_rejects_non_positive_values()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);

        Assert.Throws<ArgumentOutOfRangeException>(() => m.SetTimeSettings(0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SetTimeSettings(5, 0));
    }

    [Fact]
    public void VaultManager_export_then_import_appends_entries_with_new_ids()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);
        m.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw", Tags = { "game" } });

        var csv = m.ExportCsv();
        var original = m.Entries.Single();

        var count = m.ImportCsv(csv);

        Assert.Equal(1, count);
        Assert.Equal(2, m.Entries.Count); // 기존 + 가져온 것
        var imported = m.Entries.Last();
        Assert.Equal("Steam", imported.Title);
        Assert.Equal("game", Assert.Single(imported.Tags));
        Assert.NotEqual(original.Id, imported.Id); // 새 id 부여
    }
}
