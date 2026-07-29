using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class EntryEditViewModelTests
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

    private static VaultManager Unlocked()
    {
        var m = new VaultManager(new InMemoryStore(), Path);
        m.CreateNew(Master, Light);
        return m;
    }

    [Fact]
    public void New_entry_starts_blank_and_flags_IsNew()
    {
        var vm = new EntryEditViewModel(Unlocked());

        Assert.True(vm.IsNew);
        Assert.Equal("", vm.Title);
    }

    [Fact]
    public void Save_disabled_until_title_present()
    {
        var vm = new EntryEditViewModel(Unlocked());
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Title = "   ";
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Title = "Steam";
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Save_new_entry_adds_to_vault_and_raises_Saved()
    {
        var manager = Unlocked();
        var vm = new EntryEditViewModel(manager)
        {
            Title = "Steam",
            Login = "gamer@x.com",
            Password = "s3cr3t",
        };
        var saved = false;
        vm.Saved += (_, _) => saved = true;

        vm.SaveCommand.Execute(null);

        Assert.True(saved);
        var e = Assert.Single(manager.Entries);
        Assert.Equal("Steam", e.Title);
        Assert.Equal("gamer@x.com", e.Login);
        Assert.Equal("s3cr3t", e.Password);
    }

    [Fact]
    public void Editing_prefills_fields_from_existing()
    {
        var manager = Unlocked();
        manager.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var existing = manager.Entries[0];

        var vm = new EntryEditViewModel(manager, existing);

        Assert.False(vm.IsNew);
        Assert.Equal("Steam", vm.Title);
        Assert.Equal("gamer", vm.Login);
    }

    [Fact]
    public void Save_edit_updates_existing_and_preserves_hidden_fields()
    {
        var manager = Unlocked();
        manager.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var existing = manager.Entries[0];
        existing.TotpSecret = "TOTP-SECRET";
        existing.PasswordHistory.Add(new PasswordHistoryItem { Password = "old", ChangedAt = existing.CreatedAt });
        var id = existing.Id;

        var vm = new EntryEditViewModel(manager, existing) { Title = "Steam (main)" };
        vm.SaveCommand.Execute(null);

        var e = manager.Get(id)!;
        Assert.Single(manager.Entries);          // 새로 추가되지 않고 교체
        Assert.Equal("Steam (main)", e.Title);
        Assert.Equal("TOTP-SECRET", e.TotpSecret); // 폼에 없는 필드 보존
        Assert.Single(e.PasswordHistory);
    }

    [Fact]
    public void Cancel_leaves_existing_unchanged_and_raises_Cancelled()
    {
        var manager = Unlocked();
        manager.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var existing = manager.Entries[0];

        var vm = new EntryEditViewModel(manager, existing) { Title = "changed-but-cancelled" };
        var cancelled = false;
        vm.Cancelled += (_, _) => cancelled = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
        Assert.Equal("Steam", manager.Entries[0].Title); // 원본 무변경
    }
}
