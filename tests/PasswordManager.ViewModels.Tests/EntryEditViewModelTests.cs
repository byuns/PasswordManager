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
    public void AddTag_creates_chips_stripping_hash_and_deduping()
    {
        var vm = new EntryEditViewModel(Unlocked()) { Title = "GitHub" };

        vm.TagInput = "#work"; vm.AddTagCommand.Execute(null);
        vm.TagInput = "dev";   vm.AddTagCommand.Execute(null);
        vm.TagInput = "WORK";  vm.AddTagCommand.Execute(null); // 대소문자 무시 중복 → 무시

        Assert.Equal(new[] { "work", "dev" }, vm.Tags);
        Assert.Equal(string.Empty, vm.TagInput);
    }

    [Fact]
    public void AddTag_splits_multiple_tokens_at_once()
    {
        var vm = new EntryEditViewModel(Unlocked()) { Title = "GitHub" };

        vm.TagInput = "#work dev, 개인"; vm.AddTagCommand.Execute(null);

        Assert.Equal(new[] { "work", "dev", "개인" }, vm.Tags);
    }

    [Fact]
    public void Save_persists_tag_chips()
    {
        var manager = Unlocked();
        var vm = new EntryEditViewModel(manager) { Title = "GitHub" };
        vm.TagInput = "work"; vm.AddTagCommand.Execute(null);
        vm.TagInput = "dev";  vm.AddTagCommand.Execute(null);

        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { "work", "dev" }, Assert.Single(manager.Entries).Tags);
    }

    [Fact]
    public void Save_commits_pending_tag_input()
    {
        var manager = Unlocked();
        var vm = new EntryEditViewModel(manager) { Title = "GitHub", TagInput = "#solo" };

        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { "solo" }, Assert.Single(manager.Entries).Tags);
    }

    [Fact]
    public void RemoveTag_removes_chip()
    {
        var vm = new EntryEditViewModel(Unlocked()) { Title = "GitHub" };
        vm.TagInput = "work"; vm.AddTagCommand.Execute(null);
        vm.TagInput = "dev";  vm.AddTagCommand.Execute(null);

        vm.RemoveTagCommand.Execute("work");

        Assert.Equal(new[] { "dev" }, vm.Tags);
    }

    [Fact]
    public void Editing_prefills_tag_chips_from_existing()
    {
        var manager = Unlocked();
        manager.Add(new VaultEntry { Title = "GitHub", Password = "pw", Tags = { "work", "dev" } });

        var vm = new EntryEditViewModel(manager, manager.Entries[0]);

        Assert.Equal(new[] { "work", "dev" }, vm.Tags);
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
    public void Save_edit_changing_password_archives_previous_into_history()
    {
        var manager = Unlocked();
        manager.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "old-pw" });
        var existing = manager.Entries[0];
        var id = existing.Id;

        var vm = new EntryEditViewModel(manager, existing) { Password = "new-pw" };
        vm.SaveCommand.Execute(null);

        var e = manager.Get(id)!;
        Assert.Equal("new-pw", e.Password);
        var h = Assert.Single(e.PasswordHistory);
        Assert.Equal("old-pw", h.Password);   // 이전 비번이 이력에 적재
    }

    [Fact]
    public void Save_edit_does_not_mutate_original_before_update()
    {
        var manager = Unlocked();
        manager.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "old-pw" });
        var original = manager.Entries[0];

        // 편집 폼이 원본 객체를 직접 바꾸면 이력 감지가 깨지므로, 새 객체 전달을 보장한다.
        var vm = new EntryEditViewModel(manager, original) { Title = "renamed", Password = "new-pw" };
        vm.SaveCommand.Execute(null);

        // manager가 보관하는 항목은 교체된 새 객체여야 하고, 넘겨받은 원본 참조는 그대로여야 한다.
        Assert.NotSame(original, manager.Entries[0]);
        Assert.Equal("Steam", original.Title);
        Assert.Equal("old-pw", original.Password);
    }

    [Fact]
    public void GeneratePassword_fills_password_with_requested_length()
    {
        var vm = new EntryEditViewModel(Unlocked()) { GeneratorLength = 20 };

        vm.GeneratePasswordCommand.Execute(null);

        Assert.Equal(20, vm.Password.Length);
    }

    [Fact]
    public void GeneratePassword_respects_class_toggles()
    {
        var vm = new EntryEditViewModel(Unlocked())
        {
            GeneratorLength = 16,
            GenUppercase = false,
            GenLowercase = false,
            GenDigits = true,
            GenSymbols = false,
        };

        vm.GeneratePasswordCommand.Execute(null);

        Assert.All(vm.Password, c => Assert.True(char.IsDigit(c)));
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
