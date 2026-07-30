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

        // 볼트 경로: 기본은 %APPDATA%\PasswordManager\vault.dat. PWM_VAULT_PATH로 덮어쓸 수 있다(테스트용).
        var vaultPath = Environment.GetEnvironmentVariable("PWM_VAULT_PATH");
        if (string.IsNullOrWhiteSpace(vaultPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            vaultPath = Path.Combine(appData, "PasswordManager", "vault.dat");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(vaultPath)!);

        var manager = new VaultManager(new FileVaultStore(), vaultPath);

        // 개발용 시드: PWM_SEED=1이고 볼트가 없을 때만 태그 많은 더미 데이터로 채운다(평상시 미실행).
        if (Environment.GetEnvironmentVariable("PWM_SEED") == "1" && !manager.Exists())
            DevSeed.Populate(manager);
        var clipboard = new ClipboardCopier(new WpfClipboardService(), new DispatcherScheduler());
        var throttle = new LoginThrottle(FileLockoutStore.ForVault(vaultPath));
        var shell = new ShellViewModel(manager, clipboard: clipboard,
            appVersion: GetAppVersion(), vaultPath: vaultPath, throttle: throttle);

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
