using System.Windows.Controls;

namespace PasswordManager.App.Views;

/// <summary>
/// 언락 후 항목 목록 화면. 로직은 MainViewModel에 있고, 뷰는 카드 리스트 렌더링만 담당한다.
/// 백업/복원 등 보조 동작은 설정 화면(SettingsView)으로 이전했다(design-ux 3절).
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();
}
