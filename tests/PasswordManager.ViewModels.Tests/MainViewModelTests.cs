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

        public Task<string?> PromptAsync(string title, string message, string placeholder,
            string confirmText, string cancelText) => Task.FromResult<string?>(null);
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
    public void Verify_without_otp_requests_setup_not_verify()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));
        var target = vm.Entries[0];
        VaultEntry? verifyReq = null;
        var setupReq = false;
        vm.VerifyRequested += (_, e) => verifyReq = e;
        vm.OtpSetupRequested += (_, _) => setupReq = true;

        vm.VerifyCommand.Execute(target);

        Assert.Null(verifyReq);   // 인증 게이트가 아니라
        Assert.True(setupReq);    // OTP 등록으로 유도
    }

    [Fact]
    public void HasOtp_reflects_vault_registration()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        var vm = new MainViewModel(manager);
        Assert.False(vm.HasOtp);

        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        vm.Refresh();
        Assert.True(vm.HasOtp);
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
    public void Selected_tag_moves_to_front_and_returns_to_place_when_cleared()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("A", "a", new[] { "work" }),
            ("B", "b", new[] { "important" }),
            ("C", "c", new[] { "shop" })));

        // 기본: 사전순
        Assert.Equal(new[] { "important", "shop", "work" }, vm.AvailableTags);

        // 선택 → 가장 좌측으로, 나머지는 사전순 유지
        vm.ToggleTagCommand.Execute("work");
        Assert.Equal(new[] { "work", "important", "shop" }, vm.AvailableTags);

        // 해제 → 원래(사전순) 자리로 복귀
        vm.ToggleTagCommand.Execute("work");
        Assert.Equal(new[] { "important", "shop", "work" }, vm.AvailableTags);
    }

    [Fact]
    public void Multiple_selected_tags_sit_in_front_each_group_alphabetical()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("A", "a", new[] { "work" }),
            ("B", "b", new[] { "important" }),
            ("C", "c", new[] { "shop" }),
            ("D", "d", new[] { "alpha" })));

        vm.ToggleTagCommand.Execute("work");
        vm.ToggleTagCommand.Execute("shop");

        // 선택된 것(사전순: shop, work)이 앞, 미선택(alpha, important)이 뒤 사전순
        Assert.Equal(new[] { "shop", "work", "alpha", "important" }, vm.AvailableTags);
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
    public void Search_also_matches_tags()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" })));

        vm.SearchQuery = "game"; // 제목·아이디엔 없고 태그로만 매칭

        Assert.Single(vm.Entries);
        Assert.Equal("Steam", vm.Entries[0].Title);
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

    // --- 키보드 내비게이션 (↑/↓ 이동, Esc 필터 해제, Ctrl+B 아이디 복사) ---

    [Fact]
    public void SelectNext_without_selection_picks_the_first_entry()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));

        vm.SelectNextCommand.Execute(null);

        Assert.Same(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void SelectPrevious_without_selection_picks_the_last_entry()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));

        vm.SelectPreviousCommand.Execute(null);

        Assert.Same(vm.Entries[^1], vm.SelectedEntry);
    }

    [Fact]
    public void SelectNext_and_previous_move_one_step_in_list_order()
    {
        var vm = new MainViewModel(UnlockedWith(("A", "a"), ("B", "b"), ("C", "c")));
        vm.SelectedEntry = vm.Entries[0];

        vm.SelectNextCommand.Execute(null);
        Assert.Same(vm.Entries[1], vm.SelectedEntry);

        vm.SelectPreviousCommand.Execute(null);
        Assert.Same(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void Selection_stops_at_both_ends_without_wrapping()
    {
        var vm = new MainViewModel(UnlockedWith(("A", "a"), ("B", "b")));

        vm.SelectedEntry = vm.Entries[^1];
        vm.SelectNextCommand.Execute(null);
        Assert.Same(vm.Entries[^1], vm.SelectedEntry); // 끝에서 순환하지 않고 머문다

        vm.SelectedEntry = vm.Entries[0];
        vm.SelectPreviousCommand.Execute(null);
        Assert.Same(vm.Entries[0], vm.SelectedEntry);
    }

    [Fact]
    public void Selection_moves_within_the_filtered_list_only()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "player"), ("GitHub", "dev"), ("Google", "me")));
        vm.SearchQuery = "g"; // GitHub·Google만 남음(Steam은 제목·아이디 모두 g가 없어 제외)

        vm.SelectNextCommand.Execute(null);
        Assert.Same(vm.Entries[0], vm.SelectedEntry);
        vm.SelectNextCommand.Execute(null);
        Assert.Same(vm.Entries[1], vm.SelectedEntry);

        vm.SelectNextCommand.Execute(null); // 필터된 목록의 끝
        Assert.Same(vm.Entries[1], vm.SelectedEntry);
    }

    [Fact]
    public void SelectNext_on_empty_list_keeps_selection_null()
    {
        var vm = new MainViewModel(UnlockedWith());

        vm.SelectNextCommand.Execute(null);

        Assert.Null(vm.SelectedEntry);
    }

    [Fact]
    public void Refresh_drops_selection_that_filter_hid()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));
        vm.SelectedEntry = vm.Entries.First(e => e.Title == "Steam");

        vm.SearchQuery = "github"; // Steam이 목록에서 사라짐

        Assert.Null(vm.SelectedEntry);
    }

    [Fact]
    public void Refresh_keeps_selection_that_still_matches()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")));
        var steam = vm.Entries.First(e => e.Title == "Steam");
        vm.SelectedEntry = steam;

        vm.SearchQuery = "steam";

        Assert.Same(steam, vm.SelectedEntry);
    }

    [Fact]
    public void Refresh_follows_selection_when_entry_instance_is_replaced()
    {
        // 편집 저장은 원본을 두고 새 VaultEntry로 교체한다(TD-021). 참조로만 비교하면 선택이
        // 조용히 풀려, 돌아온 목록에서 그 행을 다시 찾아줄 수 없다(TD-049).
        var vault = UnlockedWith(("Steam", "gamer"), ("GitHub", "dev"));
        var vm = new MainViewModel(vault);
        var steam = vm.Entries.First(e => e.Title == "Steam");
        vm.Select(steam, inFavorites: false);

        vault.Update(new VaultEntry
        {
            Id = steam.Id, Title = "Steam", Login = "gamer", Password = "new-pw",
        });
        vm.Refresh();

        Assert.NotNull(vm.SelectedEntry);
        Assert.Equal(steam.Id, vm.SelectedEntry!.Id);
        Assert.NotSame(steam, vm.SelectedEntry); // 교체된 새 인스턴스를 가리켜야 한다
        Assert.Same(vm.Entries.First(e => e.Id == steam.Id), vm.SelectedEntry);
    }

    [Fact]
    public void Refresh_keeps_favorites_flag_when_instance_is_replaced()
    {
        var vault = UnlockedWith(("Steam", "gamer"));
        var vm = new MainViewModel(vault);
        var steam = vm.Entries[0];
        vault.SetPinned(steam.Id, true);
        vm.Refresh();
        vm.Select(vm.Entries[0], inFavorites: true);

        vault.Update(new VaultEntry
        {
            Id = steam.Id, Title = "Steam", Login = "gamer", Password = "new-pw",
        });
        vm.Refresh();

        Assert.Equal(steam.Id, vm.SelectedEntry?.Id);
        Assert.True(vm.SelectionInFavorites); // 핀이 유지되면 즐겨찾기 쪽 강조도 유지
    }

    [Fact]
    public void ClearFilters_resets_search_and_tags()
    {
        var vm = new MainViewModel(UnlockedWithTagged(
            ("Steam", "g", new[] { "game" }),
            ("Bank", "b", new[] { "finance" })));
        vm.ToggleTagCommand.Execute("game");
        vm.SearchQuery = "steam";

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal("", vm.SearchQuery);
        Assert.Empty(vm.SelectedTags);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void CopySelectedLogin_copies_the_selected_entrys_login()
    {
        var clip = new FakeClipboard();
        var copier = new ClipboardCopier(clip, new NoopScheduler());
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer"), ("GitHub", "dev")), copier);
        vm.SelectedEntry = vm.Entries.First(e => e.Title == "GitHub");

        vm.CopySelectedLoginCommand.Execute(null);

        Assert.Equal("dev", clip.Text);
    }

    [Fact]
    public void CopySelectedLogin_disabled_without_selection()
    {
        var clip = new FakeClipboard();
        var copier = new ClipboardCopier(clip, new NoopScheduler());
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")), copier);

        Assert.False(vm.CopySelectedLoginCommand.CanExecute(null));

        vm.SelectedEntry = vm.Entries[0];
        Assert.True(vm.CopySelectedLoginCommand.CanExecute(null));
    }

    // --- 즐겨찾기(핀) 그룹 (TD-040) ---

    [Fact]
    public void Pinned_accounts_appear_in_a_favorites_group_at_the_top()
    {
        var manager = UnlockedWith(("Steam", "main"), ("Steam", "sub"), ("GitHub", "dev"));
        var vm = new MainViewModel(manager);
        var main = vm.Entries.First(e => e.Login == "main");

        vm.TogglePinCommand.Execute(main);

        Assert.True(vm.Groups[0].IsFavorites);
        Assert.Equal(new[] { "main" }, vm.Groups[0].Accounts.Select(a => a.Login));
    }

    [Fact]
    public void Pinned_account_also_stays_in_its_own_site_group()
    {
        var manager = UnlockedWith(("Steam", "main"), ("Steam", "sub"));
        var vm = new MainViewModel(manager);

        vm.TogglePinCommand.Execute(vm.Entries.First(e => e.Login == "main"));

        // 즐겨찾기는 "바로가기"라 원래 자리에서 사라지지 않는다(TD-040 조정).
        var steam = vm.Groups.First(g => g.SiteName == "Steam");
        Assert.Equal(new[] { "main", "sub" }, steam.Accounts.Select(a => a.Login));
    }

    [Fact]
    public void TogglePin_twice_returns_the_account_to_its_site_group()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "main")));
        var main = vm.Entries[0];

        vm.TogglePinCommand.Execute(main);
        Assert.True(vm.Groups[0].IsFavorites);

        vm.TogglePinCommand.Execute(vm.Entries.First(e => e.Login == "main"));

        Assert.DoesNotContain(vm.Groups, g => g.IsFavorites);
        Assert.Equal("Steam", vm.Groups[0].SiteName);
        Assert.Single(vm.Groups); // 즐겨찾기 그룹만 사라지고 사이트 그룹은 그대로
    }

    [Fact]
    public void Selection_tracks_which_group_the_row_belongs_to()
    {
        // 핀한 계정은 즐겨찾기 그룹과 사이트 그룹 양쪽에 같은 인스턴스로 나온다.
        // 선택 강조가 한 행에만 켜지려면 "어느 그룹의 행인지"까지 구분해야 한다.
        var vm = new MainViewModel(UnlockedWith(("Steam", "main")));
        vm.TogglePinCommand.Execute(vm.Entries[0]);
        var entry = vm.Entries[0];

        vm.Select(entry, inFavorites: true);
        Assert.Same(entry, vm.SelectedEntry);
        Assert.True(vm.SelectionInFavorites);

        vm.Select(entry, inFavorites: false);
        Assert.Same(entry, vm.SelectedEntry);
        Assert.False(vm.SelectionInFavorites);
    }

    [Fact]
    public void Keyboard_movement_selects_the_row_in_the_site_group()
    {
        // ↑↓는 평면 목록(Entries) 기준이라 중복이 없다. 강조는 원래 자리(사이트 그룹)에 준다.
        var vm = new MainViewModel(UnlockedWith(("Alpha", "a"), ("Bravo", "b")));
        var alpha = vm.Entries.First(e => e.Title == "Alpha");
        vm.TogglePinCommand.Execute(alpha);
        vm.Select(vm.Entries.First(e => e.Title == "Alpha"), inFavorites: true);

        vm.SelectNextCommand.Execute(null); // 실제로 다음 항목으로 이동

        Assert.Equal("Bravo", vm.SelectedEntry?.Title);
        Assert.False(vm.SelectionInFavorites);
    }

    [Fact]
    public void Staying_at_the_end_keeps_the_current_row_highlighted()
    {
        // 이동이 일어나지 않았다면 강조도 그대로여야 한다(즐겨찾기 행에서 ↓를 눌러 끝인 경우).
        var vm = new MainViewModel(UnlockedWith(("Steam", "main")));
        vm.TogglePinCommand.Execute(vm.Entries[0]);
        vm.Select(vm.Entries[0], inFavorites: true);

        vm.SelectNextCommand.Execute(null);

        Assert.True(vm.SelectionInFavorites);
    }

    [Fact]
    public void Clearing_the_selection_also_clears_the_group_flag()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "main"), ("GitHub", "dev")));
        vm.TogglePinCommand.Execute(vm.Entries.First(e => e.Title == "Steam"));
        vm.Select(vm.Entries.First(e => e.Title == "Steam"), inFavorites: true);

        vm.SearchQuery = "github"; // 선택 항목이 필터 밖으로 나간다

        Assert.Null(vm.SelectedEntry);
        Assert.False(vm.SelectionInFavorites);
    }

    [Fact]
    public void Favorites_group_is_absent_when_nothing_is_pinned()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "main")));

        Assert.DoesNotContain(vm.Groups, g => g.IsFavorites);
    }

    [Fact]
    public void Pin_survives_refresh_because_it_is_stored_in_the_vault()
    {
        var manager = UnlockedWith(("Steam", "main"));
        var vm = new MainViewModel(manager);
        vm.TogglePinCommand.Execute(vm.Entries[0]);

        vm.Refresh();

        Assert.True(vm.Groups[0].IsFavorites);
        Assert.True(manager.Entries[0].IsPinned);
    }

    [Fact]
    public void Favorites_group_still_respects_the_search_filter()
    {
        var vm = new MainViewModel(UnlockedWith(("Steam", "main"), ("GitHub", "dev")));
        vm.TogglePinCommand.Execute(vm.Entries.First(e => e.Title == "Steam"));

        vm.SearchQuery = "github";

        Assert.DoesNotContain(vm.Groups, g => g.IsFavorites); // 검색에 안 걸린 즐겨찾기는 숨는다
        Assert.Single(vm.Groups);
    }

    // --- 정렬 (TD-040) ---

    [Fact]
    public void Default_sort_is_by_name()
    {
        var vm = new MainViewModel(UnlockedWith(("Zulu", "z"), ("Alpha", "a"), ("Mike", "m")));

        Assert.Equal(EntrySortOrder.Name, vm.SortOrder);
        Assert.Equal(new[] { "Alpha", "Mike", "Zulu" }, vm.Groups.Select(g => g.SiteName));
    }

    [Fact]
    public void Sorting_by_recently_used_puts_the_latest_first_and_unused_last()
    {
        var manager = UnlockedWith(("Alpha", "a"), ("Bravo", "b"), ("Charlie", "c"));
        var vm = new MainViewModel(manager);
        var alpha = vm.Entries.First(e => e.Title == "Alpha");
        var charlie = vm.Entries.First(e => e.Title == "Charlie");
        manager.MarkUsed(alpha.Id, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        manager.MarkUsed(charlie.Id, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        vm.SortOrder = EntrySortOrder.RecentlyUsed;

        // Charlie(5월) → Alpha(1월) → Bravo(쓴 적 없음)
        Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, vm.Groups.Select(g => g.SiteName));
    }

    [Fact]
    public void Sort_order_is_saved_to_the_vault()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        var vm = new MainViewModel(manager);

        vm.SortOrder = EntrySortOrder.RecentlyChanged;

        Assert.Equal(EntrySortOrder.RecentlyChanged, manager.SortOrder);
    }

    [Fact]
    public void Sort_order_is_read_from_the_vault_on_construction()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        manager.SetSortOrder(EntrySortOrder.RecentlyUsed);

        var vm = new MainViewModel(manager);

        Assert.Equal(EntrySortOrder.RecentlyUsed, vm.SortOrder);
    }

    [Fact]
    public void Favorites_group_stays_on_top_regardless_of_sort_order()
    {
        var manager = UnlockedWith(("Zulu", "z"), ("Alpha", "a"));
        var vm = new MainViewModel(manager);
        vm.TogglePinCommand.Execute(vm.Entries.First(e => e.Title == "Zulu"));

        vm.SortOrder = EntrySortOrder.Name; // 이름순이면 Alpha가 먼저여야 하지만

        Assert.True(vm.Groups[0].IsFavorites); // 즐겨찾기가 항상 맨 위
        Assert.Equal("Alpha", vm.Groups[1].SiteName);
    }

    [Fact]
    public void Sort_options_cover_every_order_with_labels()
    {
        var vm = new MainViewModel(UnlockedWith());

        Assert.Equal(
            Enum.GetValues<EntrySortOrder>(),
            vm.SortOptions.Select(o => o.Order));
        Assert.All(vm.SortOptions, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    // --- 삭제는 휴지통으로 (TD-041) ---

    [Fact]
    public void Delete_moves_the_entry_to_the_trash()
    {
        var manager = UnlockedWith(("Steam", "gamer"));
        var vm = new MainViewModel(manager);

        vm.DeleteCommand.Execute(vm.Entries[0]);

        Assert.Empty(vm.Entries);
        Assert.Single(manager.DeletedEntries); // 영구 삭제가 아니라 되살릴 수 있다
    }
}
