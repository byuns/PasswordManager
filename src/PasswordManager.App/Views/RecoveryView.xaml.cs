using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>비밀번호 복구 화면. 두 PasswordBox 값을 code-behind에서 ViewModel로 전달한다.</summary>
public partial class RecoveryView : UserControl
{
    public RecoveryView() => InitializeComponent();

    private void NewPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryViewModel vm)
            vm.NewPassword = NewPasswordInput.Password;
    }

    private void ConfirmInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryViewModel vm)
            vm.ConfirmPassword = ConfirmInput.Password;
    }
}
