using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>복구 키 재발급 화면. 현재 비밀번호 PasswordBox 값을 code-behind에서 ViewModel로 전달한다.</summary>
public partial class ReissueRecoveryKeyView : UserControl
{
    public ReissueRecoveryKeyView() => InitializeComponent();

    private void CurrentInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReissueRecoveryKeyViewModel vm)
            vm.CurrentPassword = CurrentInput.Password;
    }
}
