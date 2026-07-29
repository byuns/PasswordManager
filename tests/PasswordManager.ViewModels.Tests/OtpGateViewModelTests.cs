using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class OtpGateViewModelTests
{
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
