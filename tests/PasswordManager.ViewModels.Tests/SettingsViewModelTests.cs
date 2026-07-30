using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class SettingsViewModelTests
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
    public void SetupOtp_raises_OtpSetupRequested()
    {
        var vm = new SettingsViewModel(Unlocked());
        var raised = false;
        vm.OtpSetupRequested += (_, _) => raised = true;

        vm.SetupOtpCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void ChangeMasterPassword_raises_ChangeMasterRequested()
    {
        var vm = new SettingsViewModel(Unlocked());
        var raised = false;
        vm.ChangeMasterRequested += (_, _) => raised = true;

        vm.ChangeMasterPasswordCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void Backup_and_Restore_commands_raise_requests()
    {
        var vm = new SettingsViewModel(Unlocked());
        var backup = false; var restore = false;
        vm.BackupRequested += (_, _) => backup = true;
        vm.RestoreRequested += (_, _) => restore = true;

        vm.BackupCommand.Execute(null);
        vm.RestoreCommand.Execute(null);

        Assert.True(backup);
        Assert.True(restore);
    }

    [Fact]
    public void PerformBackup_then_PerformRestore_roundtrips_and_locks()
    {
        var store = new InMemoryStore();
        var m = new VaultManager(store, Path, Light);
        m.CreateNew(Master, Light);
        m.Add(new VaultEntry { Title = "Steam", Login = "gamer", Password = "pw" });
        var vm = new SettingsViewModel(m);
        var locked = false;
        vm.Locked += (_, _) => locked = true;

        vm.PerformBackup("backup.dat");
        m.Add(new VaultEntry { Title = "Later", Login = "x", Password = "pw" }); // 백업 이후 변경
        vm.PerformRestore("backup.dat");

        Assert.True(locked);            // 복원 후 잠금 화면으로
        m.Open(Master);
        Assert.Equal("Steam", Assert.Single(m.Entries).Title);
    }

    [Fact]
    public void IsOtpRegistered_reflects_vault_state_after_refresh()
    {
        var manager = Unlocked();
        var vm = new SettingsViewModel(manager);
        Assert.False(vm.IsOtpRegistered);

        manager.SetOtpSecret(TotpValidator.GenerateSecret());
        vm.Refresh();

        Assert.True(vm.IsOtpRegistered);
    }
}
