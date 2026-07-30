using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class InfoViewModelTests
{
    [Fact]
    public void Exposes_injected_name_version_and_path()
    {
        var vm = new InfoViewModel("PasswordManager", "1.2.3", @"C:\vault\vault.dat");

        Assert.Equal("PasswordManager", vm.AppName);
        Assert.Equal("1.2.3", vm.Version);
        Assert.Equal(@"C:\vault\vault.dat", vm.VaultPath);
    }

    [Fact]
    public void Defaults_are_safe_when_omitted()
    {
        var vm = new InfoViewModel();

        Assert.Equal("PasswordManager", vm.AppName);
        Assert.Equal(string.Empty, vm.Version);
        Assert.Equal(string.Empty, vm.VaultPath);
    }
}
