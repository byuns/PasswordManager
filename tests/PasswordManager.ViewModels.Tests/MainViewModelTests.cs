using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class MainViewModelTests
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

    /// <summary>언락된 상태의 매니저에 지정한 항목들을 넣어 반환한다.</summary>
    private static VaultManager UnlockedWith(params (string title, string login)[] entries)
    {
        var m = new VaultManager(new InMemoryStore(), Path);
        m.CreateNew(Master, Light);
        foreach (var (title, login) in entries)
            m.Add(new VaultEntry { Title = title, Login = login, Password = "pw" });
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
        var vm = new MainViewModel(UnlockedWith(("Steam", "gamer")));
        Assert.False(vm.EditCommand.CanExecute(null));

        vm.SelectedEntry = vm.Entries[0];
        VaultEntry? requested = null;
        vm.EditRequested += (_, e) => requested = e;
        vm.EditCommand.Execute(null);

        Assert.Same(vm.SelectedEntry, requested);
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

    [Fact]
    public void ChangeMaster_raises_ChangeMasterRequested()
    {
        var vm = new MainViewModel(UnlockedWith());
        var raised = false;
        vm.ChangeMasterRequested += (_, _) => raised = true;

        vm.ChangeMasterPasswordCommand.Execute(null);

        Assert.True(raised);
    }

    // --- OTP 등록 요청 / 열람 게이트 (design 5.4·7.4) ---

    [Fact]
    public void SetupOtp_raises_OtpSetupRequested()
    {
        var vm = new MainViewModel(UnlockedWith());
        var raised = false;
        vm.OtpSetupRequested += (_, _) => raised = true;

        vm.SetupOtpCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void IsOtpRegistered_reflects_vault_state_after_refresh()
    {
        var manager = UnlockedWith();
        var vm = new MainViewModel(manager);
        Assert.False(vm.IsOtpRegistered);

        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        vm.Refresh();

        Assert.True(vm.IsOtpRegistered);
    }

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
}
