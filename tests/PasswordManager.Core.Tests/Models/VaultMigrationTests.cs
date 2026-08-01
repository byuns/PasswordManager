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

    // --- v1 → v2 (M6 슬랙·네트워크) ---

    [Fact]
    public void V1_vault_migrates_with_offline_and_slack_off_defaults()
    {
        // 슬랙·네트워크 필드가 없던 v1 볼트 → 최신 버전으로 정규화되며 기본값(오프라인·슬랙 OFF)이 채워진다.
        var json = """{"version":1,"entries":[]}""";

        var data = VaultJson.Deserialize(json);

        Assert.Equal(VaultData.CurrentVersion, data.Version);
        Assert.False(data.NetworkAllowed);          // 전역 차단이 기본(TD-013)
        Assert.False(data.Slack.Enabled);           // 슬랙 옵트인 OFF가 기본(TD-012)
        Assert.Equal(SlackSettings.DefaultTemplate, data.Slack.MessageTemplate);
    }

    // --- v2 → v3 (즐겨찾기·최근 사용·휴지통, TD-040·TD-041) ---

    [Fact]
    public void V2_vault_migrates_to_v3_with_pin_trash_and_sort_defaults()
    {
        // v2엔 isPinned·lastUsedAt·deletedAt·sortOrder가 없다 → 기본값이 곧 기존 동작이어야 한다.
        var json = """{"version":2,"entries":[{"title":"Steam","password":"pw"}]}""";

        var data = VaultJson.Deserialize(json);
        var entry = Assert.Single(data.Entries);

        Assert.Equal(VaultData.CurrentVersion, data.Version);
        Assert.False(entry.IsPinned);                       // 고정 안 함
        Assert.Null(entry.LastUsedAt);                      // 쓴 적 없음
        Assert.Null(entry.DeletedAt);                       // 활성(휴지통 아님)
        Assert.Equal(EntrySortOrder.Name, data.SortOrder);  // 이름순이 기본
    }

    [Fact]
    public void Pin_last_used_and_trash_state_roundtrip_through_serialization()
    {
        var used = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var deleted = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var data = new VaultData { SortOrder = EntrySortOrder.RecentlyUsed };
        data.Entries.Add(new VaultEntry
        {
            Title = "Steam", Password = "pw", IsPinned = true, LastUsedAt = used, DeletedAt = deleted,
        });

        var back = VaultJson.Deserialize(VaultJson.Serialize(data));
        var entry = Assert.Single(back.Entries);

        Assert.Equal(EntrySortOrder.RecentlyUsed, back.SortOrder);
        Assert.True(entry.IsPinned);
        Assert.Equal(used, entry.LastUsedAt);
        Assert.Equal(deleted, entry.DeletedAt);
    }

    [Fact]
    public void Slack_settings_roundtrip_through_serialization()
    {
        var data = new VaultData
        {
            NetworkAllowed = true,
            Slack = new SlackSettings
            {
                Enabled = true,
                WebhookUrl = "https://hooks.slack.com/services/T/B/X",
                IncludeSiteName = true,
                MessageTemplate = "⚠ {이벤트} @ {기기명}",
                DeviceName = "데스크탑",
                NotifyPasswordChange = false,
            },
        };

        var back = VaultJson.Deserialize(VaultJson.Serialize(data));

        Assert.True(back.NetworkAllowed);
        Assert.True(back.Slack.Enabled);
        Assert.Equal("https://hooks.slack.com/services/T/B/X", back.Slack.WebhookUrl);
        Assert.True(back.Slack.IncludeSiteName);
        Assert.Equal("⚠ {이벤트} @ {기기명}", back.Slack.MessageTemplate);
        Assert.Equal("데스크탑", back.Slack.DeviceName);
        Assert.False(back.Slack.NotifyPasswordChange);
    }
}
