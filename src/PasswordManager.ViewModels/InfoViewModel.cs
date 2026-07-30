using CommunityToolkit.Mvvm.ComponentModel;

namespace PasswordManager.ViewModels;

/// <summary>
/// 정보 화면 ViewModel. 앱 이름·버전·볼트 파일 위치를 보여준다(design 7.9 "정보" 섹션).
/// 값은 App 계층(어셈블리 버전·볼트 경로)에서 셸을 통해 주입한다.
/// </summary>
public sealed partial class InfoViewModel : ObservableObject
{
    public InfoViewModel(string? appName = null, string? version = null, string? vaultPath = null)
    {
        AppName = appName ?? "PasswordManager";
        Version = version ?? string.Empty;
        VaultPath = vaultPath ?? string.Empty;
    }

    public string AppName { get; }

    public string Version { get; }

    public string VaultPath { get; }
}
