using System.Windows.Controls;

namespace PasswordManager.App.Views;

/// <summary>
/// 휴지통 화면(TD-041). 삭제한 계정을 되살리거나 영구 삭제한다.
/// 로직은 TrashViewModel에 있고, 뷰는 목록 렌더링만 담당한다.
/// </summary>
public partial class TrashView : UserControl
{
    public TrashView() => InitializeComponent();
}
