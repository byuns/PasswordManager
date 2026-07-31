using System.Text.Json;
using PasswordManager.Core.Models;

namespace PasswordManager.Core.Tests.Models;

public class VaultDataTests
{
    private static VaultEntry SampleEntry() => new()
    {
        Id = "11111111-1111-1111-1111-111111111111",
        Title = "Steam",
        Url = "https://store.steampowered.com",
        Login = "gamer@example.com",
        Password = "s3cr3t",
        Notes = "메인 계정",
        Tags = new List<string> { "게임", "메인" },
        CreatedAt = new DateTimeOffset(2026, 7, 29, 13, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 7, 29, 13, 0, 0, TimeSpan.Zero),
        LastChangedAt = new DateTimeOffset(2026, 7, 29, 13, 0, 0, TimeSpan.Zero),
        PasswordHistory = new List<PasswordHistoryItem>
        {
            new() { Password = "old-pw", ChangedAt = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero) }
        },
        TotpSecret = "JBSWY3DPEHPK3PXP",
    };

    [Fact]
    public void Roundtrip_preserves_all_fields()
    {
        var data = new VaultData { Version = VaultData.CurrentVersion, Entries = { SampleEntry() } };

        var json = VaultJson.Serialize(data);
        var back = VaultJson.Deserialize(json);

        Assert.Equal(VaultData.CurrentVersion, back.Version);
        var e = Assert.Single(back.Entries);
        var o = data.Entries[0];
        Assert.Equal(o.Id, e.Id);
        Assert.Equal(o.Title, e.Title);
        Assert.Equal(o.Url, e.Url);
        Assert.Equal(o.Login, e.Login);
        Assert.Equal(o.Password, e.Password);
        Assert.Equal(o.Notes, e.Notes);
        Assert.Equal(o.Tags, e.Tags);
        Assert.Equal(o.CreatedAt, e.CreatedAt);
        Assert.Equal(o.UpdatedAt, e.UpdatedAt);
        Assert.Equal(o.LastChangedAt, e.LastChangedAt);
        Assert.Equal(o.TotpSecret, e.TotpSecret);
        var h = Assert.Single(e.PasswordHistory);
        Assert.Equal("old-pw", h.Password);
        Assert.Equal(o.PasswordHistory[0].ChangedAt, h.ChangedAt);
    }

    [Fact]
    public void Serializes_property_names_as_camelCase()
    {
        var data = new VaultData { Version = 1, Entries = { SampleEntry() } };

        var json = VaultJson.Serialize(data);

        Assert.Contains("\"version\"", json);
        Assert.Contains("\"entries\"", json);
        Assert.Contains("\"lastChangedAt\"", json);
        Assert.Contains("\"passwordHistory\"", json);
        Assert.DoesNotContain("\"Version\"", json);
        Assert.DoesNotContain("\"LastChangedAt\"", json);
    }

    [Fact]
    public void Serializes_dates_as_iso8601_utc()
    {
        var data = new VaultData { Version = 1, Entries = { SampleEntry() } };

        var json = VaultJson.Serialize(data);

        Assert.Contains("2026-07-29T13:00:00+00:00", json);
    }

    [Fact]
    public void Deserialize_missing_collections_yields_empty_not_null()
    {
        // 오래된/최소 JSON: entries 항목에 tags·passwordHistory가 없어도 안전해야 한다.
        const string json = """
        { "version": 1, "entries": [ { "id": "x", "title": "t" } ] }
        """;

        var data = VaultJson.Deserialize(json);

        var e = Assert.Single(data.Entries);
        Assert.NotNull(e.Tags);
        Assert.Empty(e.Tags);
        Assert.NotNull(e.PasswordHistory);
        Assert.Empty(e.PasswordHistory);
        Assert.Null(e.TotpSecret);
    }

    [Fact]
    public void Deserialize_empty_object_yields_default_vault()
    {
        var data = VaultJson.Deserialize("{}");

        Assert.NotNull(data.Entries);
        Assert.Empty(data.Entries);
    }

    [Fact]
    public void NewEntry_has_unique_id_and_empty_collections()
    {
        var a = new VaultEntry();
        var b = new VaultEntry();

        Assert.False(string.IsNullOrWhiteSpace(a.Id));
        Assert.NotEqual(a.Id, b.Id);
        Assert.Empty(a.Tags);
        Assert.Empty(a.PasswordHistory);
    }
}
