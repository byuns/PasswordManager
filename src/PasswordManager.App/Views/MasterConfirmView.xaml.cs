using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 마스터 비밀번호 재확인 게이트. PasswordBox는 보안상 바인딩하지 않으므로
/// 변경 시 code-behind에서 ViewModel로 전달한다.
/// </summary>
public partial class MasterConfirmView : UserControl
{
    public MasterConfirmView() => InitializeComponent();

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MasterConfirmViewModel vm)
            vm.Password = PasswordInput.Password;
    }
}
