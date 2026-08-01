using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels.Tests;

/// <summary>휴지통 화면(TD-041): 복원·영구삭제·비우기와 남은 보관 일수.</summary>
public class TrashViewModelTests
{
    private sealed class FakeDialog : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCount { get; private set; }
        public int NotifyCount { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }

        public void Notify(string title, string message) => NotifyCount++;
    }

    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Master = "correct horse battery staple";

    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) => _files[path] = vault;
        public EncryptedVault Load(string path) => _files[path];
    }

    /// <summary>주어진 제목들을 넣고 그중 일부를 휴지통으로 보낸 매니저를 만든다.</summary>
    private static VaultManager WithTrashed(params string[] trashedTitles)
    {
        var m = new VaultManager(new InMemoryStore(), "vault.dat");
        m.CreateNew(Master, Light);
        foreach (var title in trashedTitles)
            m.Add(new VaultEntry { Title = title, Login = "id", Password = "pw" });
        foreach (var entry in m.Entries.ToList())
            m.Remove(entry.Id);
        return m;
    }

    [Fact]
    public void Lists_trashed_entries_most_recently_deleted_first()
    {
        var m = new VaultManager(new InMemoryStore(), "vault.dat");
        m.CreateNew(Master, Light);
        m.Add(new VaultEntry { Title = "A", Login = "id", Password = "pw" });
        m.Add(new VaultEntry { Title = "B", Login = "id", Password = "pw" });
        m.Remove(m.Entries.First(e => e.Title == "A").Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        m.Remove(m.Entries.First(e => e.Title == "B").Id, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        var vm = new TrashViewModel(m);

        Assert.Equal(new[] { "B", "A" }, vm.Items.Select(i => i.Entry.Title));
    }

    [Fact]
    public void IsEmpty_reflects_whether_anything_is_in_the_trash()
    {
        var empty = new VaultManager(new InMemoryStore(), "vault.dat");
        empty.CreateNew(Master, Light);

        Assert.True(new TrashViewModel(empty).IsEmpty);
        Assert.False(new TrashViewModel(WithTrashed("Steam")).IsEmpty);
    }

    [Fact]
    public void DaysLeft_counts_down_from_the_retention_window()
    {
        var m = new VaultManager(new InMemoryStore(), "vault.dat");
        m.CreateNew(Master, Light);
        m.Add(new VaultEntry { Title = "Steam", Login = "id", Password = "pw" });
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        m.Remove(m.Entries[0].Id, now.AddDays(-10));

        var vm = new TrashViewModel(m, now: () => now);

        Assert.Equal(VaultManager.TrashRetentionDays - 10, Assert.Single(vm.Items).DaysLeft);
    }

    [Fact]
    public void Restore_brings_the_entry_back_and_drops_it_from_the_list()
    {
        var m = WithTrashed("Steam");
        var vm = new TrashViewModel(m);

        vm.RestoreCommand.Execute(vm.Items[0]);

        Assert.Empty(vm.Items);
        Assert.True(vm.IsEmpty);
        Assert.Single(m.Entries); // 계정 목록으로 되살아났다
    }

    [Fact]
    public async Task Purge_asks_for_confirmation_before_erasing()
    {
        var dialog = new FakeDialog { ConfirmResult = true };
        var m = WithTrashed("Steam");
        var vm = new TrashViewModel(m, dialog);

        await vm.PurgeCommand.ExecuteAsync(vm.Items[0]);

        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Empty(vm.Items);
        Assert.Empty(m.DeletedEntries);
    }

    [Fact]
    public async Task Purge_cancelled_keeps_the_entry()
    {
        var dialog = new FakeDialog { ConfirmResult = false };
        var m = WithTrashed("Steam");
        var vm = new TrashViewModel(m, dialog);

        await vm.PurgeCommand.ExecuteAsync(vm.Items[0]);

        Assert.Single(vm.Items);
        Assert.Single(m.DeletedEntries);
    }

    [Fact]
    public async Task EmptyTrash_asks_for_confirmation_and_clears_everything()
    {
        var dialog = new FakeDialog { ConfirmResult = true };
        var m = WithTrashed("A", "B", "C");
        var vm = new TrashViewModel(m, dialog);

        await vm.EmptyTrashCommand.ExecuteAsync(null);

        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Empty(vm.Items);
        Assert.Empty(m.DeletedEntries);
    }

    [Fact]
    public async Task EmptyTrash_cancelled_keeps_everything()
    {
        var dialog = new FakeDialog { ConfirmResult = false };
        var m = WithTrashed("A", "B");
        var vm = new TrashViewModel(m, dialog);

        await vm.EmptyTrashCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(2, m.DeletedEntries.Count);
    }

    [Fact]
    public void EmptyTrash_disabled_when_already_empty()
    {
        var empty = new VaultManager(new InMemoryStore(), "vault.dat");
        empty.CreateNew(Master, Light);

        Assert.False(new TrashViewModel(empty).EmptyTrashCommand.CanExecute(null));
        Assert.True(new TrashViewModel(WithTrashed("Steam")).EmptyTrashCommand.CanExecute(null));
    }

    [Fact]
    public void Close_raises_Closed_so_the_shell_can_go_back()
    {
        var vm = new TrashViewModel(WithTrashed("Steam"));
        var raised = false;
        vm.Closed += (_, _) => raised = true;

        vm.CloseCommand.Execute(null);

        Assert.True(raised);
    }
}
