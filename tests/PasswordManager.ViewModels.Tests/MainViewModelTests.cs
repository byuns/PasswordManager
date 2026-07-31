using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels.Tests;

public class MainViewModelTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
        public void Clear() => Text = null;
    }

    private sealed class NoopScheduler : IScheduler
    {
        public void Schedule(TimeSpan delay, Action action) { }
    }

    /// <summary>확인창 결과를 미리 정해두고, 호출 횟수·마지막 토스트 메시지를 기록하는 가짜 다이얼로그.</summary>
    private sealed class FakeDialog : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCount { get; private set; }
        public int NotifyCount { get; private set; }
        public string? LastNotifyMessage { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }

        public void Notify(string title, string message)
        {
            NotifyCount++;
            LastNotifyMessage = message;
        }
    }

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

    /// <summary>언락된 상태의 매니저에 지정한 항목들을 넣어 반환한다.</summary>
    private static VaultManager UnlockedWith(params (string title, string login)[] entries)
    {
        var m = new VaultManager(new InMemoryStore(), Path);
        m.CreateNew(Master, Light);
        foreach (var (title, login) in entries)
            m.Add(new VaultEntry { Title = title, Login = login, Password = "pw" });
        return m;
    }

    /// <summary>태그가 붙은 항목들로 언락된 매니저를 만든다(태그 필터 테스트용).</summary>
    private static VaultManager UnlockedWithTagged(params (string title, string login, string[] tags)[] entries)
    {
        var m = new VaultManager(new InMemoryStore(), Path);
        m.CreateNew(Master, Light);
        foreach (var (title, login, tags) in entries)
            m.Add(new VaultEntry { Title = title, Login = login, Password = "pw", Tags = tags.ToList() });
        return m;
    }

    [Fact]
    public void Loads_all_entries_on_construction()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));

        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void SearchQuery_filters_by_title_case_insensitive()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));

        vm.SearchQuery = "git";

        Assert.Single(vm.Entries);
        Assert.Equal("GitHub", vm.Entries[0].Title);
    }

    [Fact]
    public void SearchQuery_filters_by_login_and_empty_shows_all()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer@x.com"), ("GitHub", "dev@y.com")));

        vm.SearchQuery = "gamer";
        Assert.Single(vm.Entries);

        vm.SearchQuery = "";
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void Delete_removes_selected_and_refreshes()
    {
        var manager = UnlockedWith(("Steam", "gamer"), ("GitHub", "dev"));
        var vm = new MainViewModel(manager);
        vm.SelectedEntry = vm.Entries.First(e => e.Title == "Steam");

        vm.DeleteCommand.Execute(null);

        Assert.Single(vm.Entries);
        Assert.Equal("GitHub", vm.Entries[0].Title);
        Assert.Null(vm.SelectedEntry);
    }

    [Fact]
    public void Delete_asks_confirmation_and_removes_and_notifies_when_confirmed()
    {
        var dialog = new FakeDialog { ConfirmResult = true };
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")), dialog: dialog);
        var target = vm.Entries.First(e => e.Title == "Steam");

        vm.DeleteCommand.Execute(target);

        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Single(vm.Entries);
        Assert.Equal("GitHub", vm.Entries[0].Title);
        Assert.Equal(1, dialog.NotifyCount);
    }

    [Fact]
    public void Delete_cancelled_keeps_entry_and_does_not_notify()
    {
        var dialog = new FakeDialog { ConfirmResult = false };
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")), dialog: dialog);
        var target = vm.Entries.First(e => e.Title == "Steam");

        vm.DeleteCommand.Execute(target);

        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal(0, dialog.NotifyCount);
    }

    [Fact]
    public void Delete_disabled_without_selection()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));

        Assert.False(vm.DeleteCommand.CanExecute(null));

        vm.SelectedEntry = vm.Entries[0];
        Assert.True(vm.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Lock_locks_vault_and_raises_Locked()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        var vm = new MainViewModel(manager);
        var raised = false;
        vm.Locked += (_, _) => raised = true;

        vm.LockCommand.Execute(null);

        Assert.True(raised);
        Assert.False(manager.IsUnlocked);
    }

    [Fact]
    public void Edit_raises_EditRequested_with_selected_entry()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var vm = new MainViewModel(manager);
        Assert.False(vm.EditCommand.CanExecute(null));

        vm.SelectedEntry = vm.Entries[0];
        VaultEntry? requested = null;
        vm.EditRequested += (_, e) => requested = e;
        vm.EditCommand.Execute(null);

        Assert.Same(vm.SelectedEntry, requested);
    }

    [Fact]
    public void Edit_without_otp_shows_hint_and_does_not_request()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));
        vm.SelectedEntry = vm.Entries[0];
        VaultEntry? requested = null;
        vm.EditRequested += (_, e) => requested = e;

        vm.EditCommand.Execute(null);

        Assert.Null(requested);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void Same_site_accounts_are_grouped_together()
    {
        var vm = new MainViewModel(UnlockedWith(
            ("GitHub", "personal"), ("Google", "me"), ("GitHub", "work")));

        Assert.Equal(2, vm.Groups.Count);
        var github = vm.Groups.First(g => g.SiteName == "GitHub");
        Assert.Equal(2, github.Accounts.Count);
        Assert.True(github.HasMultipleAccounts);
        Assert.Equal(new[] { "personal", "work" }, github.Accounts.Select(a => a.Login));
    }

    [Fact]
    public void Site_grouping_is_case_insensitive()
    {
        var vm = new MainViewModel(UnlockedWith(("github", "a"), ("GitHub", "b")));

        Assert.Single(vm.Groups);
        Assert.Equal(2, vm.Groups[0].Accounts.Count);
    }

    [Fact]
    public void Single_account_site_is_a_group_of_one()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));

        Assert.Single(vm.Groups);
        Assert.False(vm.Groups[0].HasMultipleAccounts);
    }

    [Fact]
    public void Search_filters_then_regroups()
    {
        var vm = new MainViewModel(UnlockedWith(
            ("GitHub", "personal"), ("GitHub", "work"), ("Google", "me")));

        vm.SearchQuery = "google";

        Assert.Single(vm.Groups);
        Assert.Equal("Google", vm.Groups[0].SiteName);
    }

    [Fact]
    public void CopyLogin_copies_the_login_to_clipboard()
    {
        var clip = new FakeClipboard();
        var copier = new ClipboardCopier(clip, new NoopScheduler());
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")), copier);

        vm.CopyLoginCommand.Execute("gamer");

        Assert.Equal("gamer", clip.Text);
    }

    [Fact]
    public void NewEntry_raises_AddRequested()
    {
        var vm = new MainViewModel(UnlockedWith());
        var raised = false;
        vm.AddRequested += (_, _) => raised = true;

        vm.NewEntryCommand.Execute(null);

        Assert.True(raised);
    }

    // --- 열람 게이트 (design 5.4·7.4) ---

    [Fact]
    public void Reveal_disabled_without_selection()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));
        Assert.False(vm.RevealCommand.CanExecute(null));

        vm.SelectedEntry = vm.Entries[0];
        Assert.True(vm.RevealCommand.CanExecute(null));
    }

    [Fact]
    public void Reveal_without_otp_shows_hint_and_does_not_request()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));
        vm.SelectedEntry = vm.Entries[0];
        VaultEntry? requested = null;
        vm.RevealRequested += (_, e) => requested = e;

        vm.RevealCommand.Execute(null);

        Assert.Null(requested);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void Reveal_with_otp_raises_RevealRequested_with_selected_entry()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var vm = new MainViewModel(manager);
        vm.SelectedEntry = vm.Entries[0];
        VaultEntry? requested = null;
        vm.RevealRequested += (_, e) => requested = e;

        vm.RevealCommand.Execute(null);

        Assert.Same(vm.SelectedEntry, requested);
    }

    // --- 인증 게이트 (행별 단일 '인증' 버튼 → 통과 후 보기/편집/삭제) ---

    [Fact]
    public void Verify_with_otp_raises_VerifyRequested_with_that_entry()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var vm = new MainViewModel(manager);
        var target = vm.Entries[0];
        VaultEntry? requested = null;
        vm.VerifyRequested += (_, e) => requested = e;

        vm.VerifyCommand.Execute(target);

        Assert.Same(target, requested);
    }

    [Fact]
    public void Verify_without_otp_shows_hint_and_does_not_request()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));
        var target = vm.Entries[0];
        VaultEntry? requested = null;
        vm.VerifyRequested += (_, e) => requested = e;

        vm.VerifyCommand.Execute(target);

        Assert.Null(requested);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    // --- 카드 내 액션(항목을 인자로 직접 전달, design-ux §4) ---

    [Fact]
    public void Delete_with_parameter_removes_that_entry_regardless_of_selection()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));
        var target = vm.Entries.First(e => e.Title == "Steam");

        vm.DeleteCommand.Execute(target);

        Assert.Single(vm.Entries);
        Assert.Equal("GitHub", vm.Entries[0].Title);
    }

    [Fact]
    public void Delete_with_parameter_enabled_without_selection()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));

        Assert.True(vm.DeleteCommand.CanExecute(vm.Entries[0]));
    }

    [Fact]
    public void Edit_with_parameter_raises_EditRequested_with_that_entry()
    {
        var manager = UnlockedWith(("Steam", "gamer"), ("GitHub", "dev"));
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var vm = new MainViewModel(manager);
        var target = vm.Entries.First(e => e.Title == "GitHub");
        VaultEntry? requested = null;
        vm.EditRequested += (_, e) => requested = e;

        vm.EditCommand.Execute(target);

        Assert.Same(target, requested);
    }

    [Fact]
    public void Reveal_with_parameter_and_otp_raises_with_that_entry()
    {
        var manager = UnlockedWith(("Steam", "gamer"), ("GitHub", "dev"));
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var vm = new MainViewModel(manager);
        var target = vm.Entries.First(e => e.Title == "GitHub");
        VaultEntry? requested = null;
        vm.RevealRequested += (_, e) => requested = e;

        vm.RevealCommand.Execute(target);

        Assert.Same(target, requested);
    }

    // --- 태그 필터 (TD-029: 선택 태그끼리 OR 합집합, 검색어와는 AND) ---

    [Fact]
    public void AvailableTags_lists_distinct_tags_across_all_entries()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("A", "a", new[] { "work", "important" }),
            ("B", "b", new[] { "shop", "work" })));

        Assert.Equal(new[] { "important", "shop", "work" }, vm.AvailableTags.OrderBy(t => t));
    }

    [Fact]
    public void ToggleTag_filters_entries_by_that_tag()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" })));

        vm.ToggleTagCommand.Execute("game");

        Assert.Single(vm.Entries);
        Assert.Equal("Steam", vm.Entries[0].Title);
        Assert.Contains("game", vm.SelectedTags);
    }

    [Fact]
    public void Multiple_selected_tags_use_OR_union()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" }),
            ("News", "n", new[] { "read" })));

        vm.ToggleTagCommand.Execute("game");
        vm.ToggleTagCommand.Execute("finance");

        Assert.Equal(2, vm.Entries.Count);
        Assert.Contains(vm.Entries, e => e.Title == "Steam");
        Assert.Contains(vm.Entries, e => e.Title == "Bank");
    }

    [Fact]
    public void Tag_filter_combines_with_search_as_AND()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("GameStop", "shop", new[] { "game" })));

        vm.ToggleTagCommand.Execute("game"); // 둘 다 game
        vm.SearchQuery = "steam";            // 그 중 steam만

        Assert.Single(vm.Entries);
        Assert.Equal("Steam", vm.Entries[0].Title);
    }

    [Fact]
    public void Toggling_tag_off_clears_that_filter()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" })));

        vm.ToggleTagCommand.Execute("game");
        Assert.Single(vm.Entries);

        vm.ToggleTagCommand.Execute("game"); // 다시 눌러 해제
        Assert.Equal(2, vm.Entries.Count);
        Assert.DoesNotContain("game", vm.SelectedTags);
    }

    [Fact]
    public void ClearTags_removes_all_tag_filters()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" })));
        vm.ToggleTagCommand.Execute("game");
        vm.ToggleTagCommand.Execute("finance");

        vm.ClearTagsCommand.Execute(null);

        Assert.Empty(vm.SelectedTags);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void Deleting_last_entry_of_a_tag_prunes_it_from_selection()
    {
        var manager = UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" }));
        var vm = new MainViewModel(manager);
        vm.ToggleTagCommand.Execute("game");

        vm.DeleteCommand.Execute(vm.Entries.First(e => e.Title == "Steam"));

        Assert.DoesNotContain("game", vm.AvailableTags);
        Assert.DoesNotContain("game", vm.SelectedTags);
        Assert.Single(vm.Entries); // 태그 필터가 걷혀 남은 Bank가 보인다
    }
}
