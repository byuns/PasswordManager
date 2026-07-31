using CommunityToolkit.Mvvm.ComponentModel;

namespace PasswordManager.ViewModels;

/// <summary>
/// 사용 안내(메뉴얼) 화면 ViewModel. 내용은 정적이라 View(ManualView)가 직접 문구를 담고,
/// 이 VM은 셸 네비게이션이 가리키는 마커 역할만 한다(InfoViewModel과 같은 결).
/// </summary>
public sealed partial class ManualViewModel : ObservableObject
{
}
