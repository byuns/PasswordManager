using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

/// <summary>휴지통(소프트 삭제)·즐겨찾기·최근 사용·정렬 설정 (TD-040·TD-041).</summary>
public class VaultTrashAndPinTests
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

    private static VaultManager Unlocked(InMemoryStore store, params string[] titles)
    {
        var m = new VaultManager(store, Path);
        m.CreateNew(Master, Light);
        foreach (var title in titles)
            m.Add(new VaultEntry { Title = title, Login = "id", Password = "pw" });
        return m;
    }

    // --- 휴지통: 소프트 삭제 ---

    [Fact]
    public void Remove_moves_entry_to_trash_instead_of_erasing_it()
    {
        var m = Unlocked(new InMemoryStore(), "Steam", "GitHub");
        var steam = m.Entries.First(e => e.Title == "Steam");

        m.Remove(steam.Id);

        Assert.DoesNotContain(m.Entries, e => e.Title == "Steam"); // 목록에서는 사라지고
        Assert.Contains(m.DeletedEntries, e => e.Title == "Steam"); // 휴지통에 남는다
        Assert.Single(m.Entries);
    }

    [Fact]
    public void Trash_survives_lock_and_reopen()
    {
        var store = new InMemoryStore();
        var m = Unlocked(store, "Steam");
        m.Remove(m.Entries[0].Id);
        m.Lock();

        var reopened = new VaultManager(store, Path);
        reopened.Open(Master);

        Assert.Empty(reopened.Entries);
        Assert.Single(reopened.DeletedEntries);
    }

    [Fact]
    public void DeletedEntries_are_ordered_most_recently_deleted_first()
    {
        var m = Unlocked(new InMemoryStore(), "A", "B", "C");
        var a = m.Entries.First(e => e.Title == "A");
        var b = m.Entries.First(e => e.Title == "B");

        m.Remove(a.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        m.Remove(b.Id, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new[] { "B", "A" }, m.DeletedEntries.Select(e => e.Title));
    }

    [Fact]
    public void RestoreEntry_brings_it_back_to_the_list()
    {
        var m = Unlocked(new InMemoryStore(), "Steam");
        var id = m.Entries[0].Id;
        m.Remove(id);

        m.RestoreEntry(id);

        Assert.Single(m.Entries);
        Assert.Empty(m.DeletedEntries);
        Assert.Null(m.Entries[0].DeletedAt);
    }

    [Fact]
    public void PurgeEntry_erases_it_for_good()
    {
        var m = Unlocked(new InMemoryStore(), "Steam", "GitHub");
        var id = m.Entries.First(e => e.Title == "Steam").Id;
        m.Remove(id);

        m.PurgeEntry(id);

        Assert.Empty(m.DeletedEntries);
        Assert.Single(m.Entries); // 활성 항목은 그대로
        Assert.Null(m.Get(id));
    }

    [Fact]
    public void EmptyTrash_clears_deleted_only()
    {
        var m = Unlocked(new InMemoryStore(), "A", "B", "C");
        m.Remove(m.Entries.First(e => e.Title == "A").Id);
        m.Remove(m.Entries.First(e => e.Title == "B").Id);

        m.EmptyTrash();

        Assert.Empty(m.DeletedEntries);
        Assert.Single(m.Entries);
        Assert.Equal("C", m.Entries[0].Title);
    }

    [Fact]
    public void Get_does_not_return_a_trashed_entry()
    {
        var m = Unlocked(new InMemoryStore(), "Steam");
        var id = m.Entries[0].Id;

        m.Remove(id);

        Assert.Null(m.Get(id));
    }

    [Fact]
    public void ExportCsv_leaves_out_trashed_entries()
    {
        var m = Unlocked(new InMemoryStore(), "Steam", "GitHub");
        m.Remove(m.Entries.First(e => e.Title == "Steam").Id);

        var csv = m.ExportCsv();

        Assert.DoesNotContain("Steam", csv);
        Assert.Contains("GitHub", csv);
    }

    // --- 휴지통: 30일 자동 정리 ---

    [Fact]
    public void PurgeExpiredTrash_removes_only_entries_past_the_retention_window()
    {
        var m = Unlocked(new InMemoryStore(), "Old", "Fresh");
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        m.Remove(m.Entries.First(e => e.Title == "Old").Id, now.AddDays(-VaultManager.TrashRetentionDays - 1));
        m.Remove(m.Entries.First(e => e.Title == "Fresh").Id, now.AddDays(-1));

        var purged = m.PurgeExpiredTrash(now);

        Assert.Equal(1, purged);
        Assert.Equal(new[] { "Fresh" }, m.DeletedEntries.Select(e => e.Title));
    }

    [Fact]
    public void PurgeExpiredTrash_keeps_an_entry_deleted_exactly_at_the_boundary()
    {
        var m = Unlocked(new InMemoryStore(), "Edge");
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        m.Remove(m.Entries[0].Id, now.AddDays(-VaultManager.TrashRetentionDays));

        var purged = m.PurgeExpiredTrash(now);

        Assert.Equal(0, purged); // 딱 30일째는 아직 남긴다(초과분만 정리)
        Assert.Single(m.DeletedEntries);
    }

    [Fact]
    public void Opening_a_vault_purges_expired_trash()
    {
        var store = new InMemoryStore();
        var m = Unlocked(store, "Old");
        m.Remove(m.Entries[0].Id, DateTimeOffset.UtcNow.AddDays(-VaultManager.TrashRetentionDays - 1));
        m.Lock();

        var reopened = new VaultManager(store, Path);
        reopened.Open(Master);

        Assert.Empty(reopened.DeletedEntries);
    }

    // --- 즐겨찾기(핀) ---

    [Fact]
    public void SetPinned_toggles_and_persists()
    {
        var store = new InMemoryStore();
        var m = Unlocked(store, "Steam");
        var id = m.Entries[0].Id;

        m.SetPinned(id, true);
        Assert.True(m.Entries[0].IsPinned);

        m.Lock();
        var reopened = new VaultManager(store, Path);
        reopened.Open(Master);
        Assert.True(reopened.Entries[0].IsPinned);

        reopened.SetPinned(id, false);
        Assert.False(reopened.Entries[0].IsPinned);
    }

    // --- 최근 사용 ---

    [Fact]
    public void MarkUsed_stamps_last_used_at()
    {
        var m = Unlocked(new InMemoryStore(), "Steam");
        var id = m.Entries[0].Id;
        var when = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        Assert.Null(m.Entries[0].LastUsedAt);
        m.MarkUsed(id, when);

        Assert.Equal(when, m.Entries[0].LastUsedAt);
    }

    // --- 편집이 앱 소유 필드를 지우지 않는다 ---

    [Fact]
    public void Update_carries_over_pin_last_used_and_deleted_state()
    {
        var m = Unlocked(new InMemoryStore(), "Steam");
        var original = m.Entries[0];
        var used = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        m.SetPinned(original.Id, true);
        m.MarkUsed(original.Id, used);

        // 편집 화면은 폼 값으로 새 VaultEntry를 만들어 넘긴다 → 앱 소유 필드가 리셋되면 안 된다.
        m.Update(new VaultEntry
        {
            Id = original.Id,
            Title = "Steam",
            Login = "changed@example.com",
            Password = "pw",
        });

        var updated = m.Entries[0];
        Assert.Equal("changed@example.com", updated.Login);
        Assert.True(updated.IsPinned);
        Assert.Equal(used, updated.LastUsedAt);
        Assert.Null(updated.DeletedAt);
    }

    // --- 정렬 설정 저장 ---

    [Fact]
    public void SortOrder_defaults_to_name_and_persists_when_changed()
    {
        var store = new InMemoryStore();
        var m = Unlocked(store, "Steam");

        Assert.Equal(EntrySortOrder.Name, m.SortOrder);

        m.SetSortOrder(EntrySortOrder.RecentlyUsed);
        m.Lock();

        var reopened = new VaultManager(store, Path);
        reopened.Open(Master);
        Assert.Equal(EntrySortOrder.RecentlyUsed, reopened.SortOrder);
    }
}
