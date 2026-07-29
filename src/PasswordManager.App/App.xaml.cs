using System.IO;
using System.Threading;
using System.Windows;
using PasswordManager.Core.Vault;
using PasswordManager.Storage;
using PasswordManager.ViewModels;

namespace PasswordManager.App;

/// <summary>
/// 앱 진입점. 단일 인스턴스(Mutex, TD-007)를 보장하고, 볼트 경로를
/// %APPDATA%\PasswordManager\vault.dat로 잡아 ShellViewModel을 구성해 창을 띄운다.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\PasswordManager.SingleInstance";
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("PasswordManager가 이미 실행 중입니다.", "PasswordManager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "PasswordManager");
        Directory.CreateDirectory(directory);
        var vaultPath = Path.Combine(directory, "vault.dat");

        var manager = new VaultManager(new FileVaultStore(), vaultPath);
        var shell = new ShellViewModel(manager);

        new ShellWindow { DataContext = shell }.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
