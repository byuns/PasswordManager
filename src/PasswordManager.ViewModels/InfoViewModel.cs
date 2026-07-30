using CommunityToolkit.Mvvm.ComponentModel;

namespace PasswordManager.ViewModels;

/// <summary>
/// 정보 화면 ViewModel(뼈대). 앱 버전·볼트 파일 위치를 보여준다
/// (design 7.9 "정보" 섹션). 실제 값 주입은 M6 5단계에서 채운다.
/// </summary>
public sealed partial class InfoViewModel : ObservableObject
{
    public InfoViewModel(string? appName = null, string? vaultPath = null)
    {
        AppName = appName ?? "PasswordManager";
        VaultPath = vaultPath ?? string.Empty;
    }

    public string AppName { get; }

    public string VaultPath { get; }
}
