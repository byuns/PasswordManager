using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>마스터 비밀번호 변경 화면. 세 PasswordBox 값을 code-behind에서 ViewModel로 전달한다.</summary>
public partial class ChangeMasterPasswordView : UserControl
{
    public ChangeMasterPasswordView() => InitializeComponent();

    private void CurrentInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm)
            vm.CurrentPassword = CurrentInput.Password;
    }

    private void NewPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm)
            vm.NewPassword = NewPasswordInput.Password;
    }

    private void ConfirmInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChangeMasterPasswordViewModel vm)
            vm.ConfirmPassword = ConfirmInput.Password;
    }
}
