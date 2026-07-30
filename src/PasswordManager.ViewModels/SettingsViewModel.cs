using CommunityToolkit.Mvvm.ComponentModel;

namespace PasswordManager.ViewModels;

/// <summary>
/// 설정 화면 ViewModel(뼈대). 상단 툴바에 몰려 있던 보조 동작
/// (OTP 등록/재설정 · 마스터 변경 · 자동잠금/클립보드 시간 · 백업/복원)을
/// M6 3단계에서 이곳으로 이전한다(design-ux 3절, S7).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
}
