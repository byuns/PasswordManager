using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using PasswordManager.App.Services;
using PasswordManager.Core.Vault;
using PasswordManager.Storage;
using PasswordManager.ViewModels;
using Wpf.Ui.Appearance;

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

        // 강조색을 인디고(#6366F1)로 오버라이드한다(TD-023). Dark 기준으로 파생색을 자동 계산한다.
        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#6366F1"), ApplicationTheme.Dark);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "PasswordManager");
        Directory.CreateDirectory(directory);
        var vaultPath = Path.Combine(directory, "vault.dat");

        var manager = new VaultManager(new FileVaultStore(), vaultPath);
        var clipboard = new ClipboardCopier(new WpfClipboardService(), new DispatcherScheduler());
        var shell = new ShellViewModel(manager, clipboard: clipboard,
            appVersion: GetAppVersion(), vaultPath: vaultPath);

        new ShellWindow { DataContext = shell }.Show();
    }

    /// <summary>어셈블리의 정보 버전(없으면 파일 버전)을 사람이 읽는 문자열로 돌려준다.</summary>
    private static string GetAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return info ?? asm.GetName().Version?.ToString() ?? "";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
