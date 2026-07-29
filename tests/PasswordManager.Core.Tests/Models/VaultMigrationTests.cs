using PasswordManager.Core.Models;

namespace PasswordManager.Core.Tests.Models;

public class VaultMigrationTests
{
    [Fact]
    public void Roundtrips_current_version()
    {
        var data = new VaultData { Version = VaultData.CurrentVersion };
        data.Entries.Add(new VaultEntry { Title = "Steam", Password = "pw" });

        var back = VaultJson.Deserialize(VaultJson.Serialize(data));

        Assert.Equal(VaultData.CurrentVersion, back.Version);
        Assert.Equal("Steam", Assert.Single(back.Entries).Title);
    }

    [Fact]
    public void Rejects_file_from_newer_app_version()
    {
        // 더 새 버전의 앱이 쓴 파일 → 조용히 깨뜨리지 말고 명확히 거부(TD-008).
        var json = $$"""{"version":{{VaultData.CurrentVersion + 1}},"entries":[]}""";

        Assert.Throws<VaultVersionException>(() => VaultJson.Deserialize(json));
    }

    [Fact]
    public void Missing_version_is_treated_as_v1_legacy()
    {
        var json = """{"entries":[{"title":"Old","password":"pw"}]}""";

        var data = VaultJson.Deserialize(json);

        Assert.Equal(VaultData.CurrentVersion, data.Version); // 정규화
        Assert.Equal("Old", Assert.Single(data.Entries).Title);
    }

    [Fact]
    public void Missing_new_fields_fill_with_defaults()
    {
        // 관대한 파싱: 옛 파일에 tags·passwordHistory가 없어도 빈 목록으로 읽힌다(TD-008).
        var json = """{"version":1,"entries":[{"title":"NoTags","password":"pw"}]}""";

        var entry = Assert.Single(VaultJson.Deserialize(json).Entries);

        Assert.Empty(entry.Tags);
        Assert.Empty(entry.PasswordHistory);
    }
}
