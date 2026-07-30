using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels.Tests;

public class OtpGateViewModelTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
        public void Clear() => Text = null;
    }

    private sealed class ImmediateScheduler : IScheduler
    {
        public void Schedule(TimeSpan delay, Action action) { /* 예약만, 즉시 실행 안 함 */ }
    }

    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);
    private const string Master = "correct horse battery staple";
    private const string Path = "vault.dat";

    private sealed class InMemoryStore : IVaultFileStore
    {
        private readonly Dictionary<string, EncryptedVault> _files = new();
        public bool Exists(string path) => _files.ContainsKey(path);
        public void Save(string path, EncryptedVault vault) => _files[path] = vault;
        public EncryptedVault Load(string path) => _files[path];
    }

    private static (OtpGateViewModel vm, VaultEntry entry, string secret) NewVm()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        manager.CreateNew(Master, Light);
        var secret = TotpValidator.GenerateSecret();
        manager.SetOtpSecret(secret);
        var entry = new VaultEntry { Title = "Steam", Login = "gamer", Password = "s3cr3t" };
        manager.Add(entry);
        return (new OtpGateViewModel(manager, entry, () => FixedNow), entry, secret);
    }

    [Fact]
    public void Password_is_hidden_before_verification()
    {
        var (vm, _, _) = NewVm();

        Assert.Null(vm.RevealedPassword);
        Assert.Equal("Steam", vm.Title);
    }

    [Fact]
    public void Reveal_disabled_until_code_entered()
    {
        var (vm, _, _) = NewVm();
        Assert.False(vm.RevealCommand.CanExecute(null));

        vm.VerificationCode = "123456";
        Assert.True(vm.RevealCommand.CanExecute(null));
    }

    [Fact]
    public void Reveal_with_valid_code_exposes_password_and_raises_Revealed()
    {
        var (vm, entry, secret) = NewVm();
        var raised = false;
        vm.Revealed += (_, _) => raised = true;

        vm.VerificationCode = TotpValidator.GenerateCode(secret, FixedNow);
        vm.RevealCommand.Execute(null);

        Assert.True(raised);
        Assert.Equal(entry.Password, vm.RevealedPassword);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Reveal_with_invalid_code_sets_error_and_keeps_password_hidden()
    {
        var (vm, _, _) = NewVm();
        var raised = false;
        vm.Revealed += (_, _) => raised = true;

        vm.VerificationCode = "000000";
        vm.RevealCommand.Execute(null);

        Assert.False(raised);
        Assert.Null(vm.RevealedPassword);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public void Password_history_is_hidden_before_verification()
    {
        var (vm, entry, _) = NewVm();
        entry.PasswordHistory.Add(new PasswordHistoryItem { Password = "old-1", ChangedAt = FixedNow });

        Assert.Empty(vm.RevealedHistory);
    }

    [Fact]
    public void Reveal_with_valid_code_exposes_password_history()
    {
        var (vm, entry, secret) = NewVm();
        entry.PasswordHistory.Add(new PasswordHistoryItem { Password = "old-2", ChangedAt = FixedNow });
        entry.PasswordHistory.Add(new PasswordHistoryItem { Password = "old-1", ChangedAt = FixedNow });

        vm.VerificationCode = TotpValidator.GenerateCode(secret, FixedNow);
        vm.RevealCommand.Execute(null);

        Assert.Equal(2, vm.RevealedHistory.Count);
        Assert.Equal("old-2", vm.RevealedHistory[0].Password); // 순서 보존(최신이 앞)
    }

    [Fact]
    public void Reveal_with_invalid_code_keeps_history_hidden()
    {
        var (vm, entry, _) = NewVm();
        entry.PasswordHistory.Add(new PasswordHistoryItem { Password = "old-1", ChangedAt = FixedNow });

        vm.VerificationCode = "000000";
        vm.RevealCommand.Execute(null);

        Assert.Empty(vm.RevealedHistory);
    }

    [Fact]
    public void Copy_command_copies_given_password_to_clipboard()
    {
        var clip = new FakeClipboard();
        var copier = new ClipboardCopier(clip, new ImmediateScheduler());
        var manager = new VaultManager(new InMemoryStore(), Path);
        manager.CreateNew(Master, Light);
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var entry = new VaultEntry { Title = "Steam", Password = "s3cr3t" };
        manager.Add(entry);
        var vm = new OtpGateViewModel(manager, entry, () => FixedNow, copier);

        vm.CopyCommand.Execute("s3cr3t");

        Assert.Equal("s3cr3t", clip.Text);
    }

    [Fact]
    public void Copy_command_is_noop_without_clipboard()
    {
        var (vm, _, _) = NewVm(); // copier 미주입
        vm.CopyCommand.Execute("s3cr3t"); // 예외 없이 무시
    }

    [Fact]
    public void Edit_purpose_valid_code_raises_Verified_without_revealing()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        manager.CreateNew(Master, Light);
        var secret = TotpValidator.GenerateSecret();
        manager.SetOtpSecret(secret);
        var entry = new VaultEntry { Title = "Steam", Password = "s3cr3t" };
        manager.Add(entry);
        var vm = new OtpGateViewModel(manager, entry, () => FixedNow, purpose: OtpGatePurpose.Edit);
        var verified = false;
        var revealed = false;
        vm.Verified += (_, _) => verified = true;
        vm.Revealed += (_, _) => revealed = true;

        vm.VerificationCode = TotpValidator.GenerateCode(secret, FixedNow);
        vm.RevealCommand.Execute(null);

        Assert.True(verified);
        Assert.False(revealed);
        Assert.Null(vm.RevealedPassword); // 편집 용도는 비번을 노출하지 않는다
        Assert.Equal("확인", vm.ActionLabel);
    }

    [Fact]
    public void PreVerified_reveal_shows_password_immediately_without_code()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        manager.CreateNew(Master, Light);
        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        var entry = new VaultEntry { Title = "Steam", Password = "s3cr3t" };
        manager.Add(entry);

        var vm = new OtpGateViewModel(manager, entry, () => FixedNow, preVerified: true);

        Assert.False(vm.RequiresCode);            // 코드 입력란 숨김
        Assert.Equal("s3cr3t", vm.RevealedPassword); // 즉시 노출
    }

    [Fact]
    public void Cancel_raises_Cancelled_without_revealing()
    {
        var (vm, _, _) = NewVm();
        var cancelled = false;
        vm.Cancelled += (_, _) => cancelled = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
        Assert.Null(vm.RevealedPassword);
    }
}
