using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class OtpSetupViewModelTests
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

    private static (OtpSetupViewModel vm, VaultManager manager) NewVm()
    {
        var manager = new VaultManager(new InMemoryStore(), Path);
        manager.CreateNew(Master, Light);
        return (new OtpSetupViewModel(manager, () => FixedNow), manager);
    }

    [Fact]
    public void Construction_exposes_secret_and_otpauth_uri()
    {
        var (vm, _) = NewVm();

        Assert.False(string.IsNullOrWhiteSpace(vm.Secret));
        Assert.StartsWith("otpauth://totp/", vm.OtpAuthUri);
        Assert.Contains(vm.Secret, vm.OtpAuthUri);
    }

    [Fact]
    public void Construction_does_not_register_before_confirm()
    {
        var (_, manager) = NewVm();

        Assert.False(manager.HasOtp);
    }

    [Fact]
    public void Confirm_disabled_until_code_entered()
    {
        var (vm, _) = NewVm();
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.VerificationCode = "123456";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Confirm_with_valid_code_persists_secret_and_completes()
    {
        var (vm, manager) = NewVm();
        var completed = false;
        vm.Completed += (_, _) => completed = true;

        vm.VerificationCode = TotpValidator.GenerateCode(vm.Secret, FixedNow);
        vm.ConfirmCommand.Execute(null);

        Assert.True(completed);
        Assert.True(manager.HasOtp);
        Assert.Null(vm.ErrorMessage);
        Assert.True(manager.VerifyOtp(TotpValidator.GenerateCode(vm.Secret, FixedNow), FixedNow));
    }

    [Fact]
    public void Confirm_with_invalid_code_sets_error_and_does_not_persist()
    {
        var (vm, manager) = NewVm();
        var completed = false;
        vm.Completed += (_, _) => completed = true;

        vm.VerificationCode = "000000";
        vm.ConfirmCommand.Execute(null);

        Assert.False(completed);
        Assert.False(manager.HasOtp);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public void Cancel_fires_cancelled_and_does_not_persist()
    {
        var (vm, manager) = NewVm();
        var cancelled = false;
        vm.Cancelled += (_, _) => cancelled = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
        Assert.False(manager.HasOtp);
    }
}
