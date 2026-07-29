using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>새 볼트 생성 화면. 두 PasswordBox 값을 code-behind에서 ViewModel로 전달한다.</summary>
public partial class CreateVaultView : UserControl
{
    public CreateVaultView() => InitializeComponent();

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateVaultViewModel vm)
            vm.Password = PasswordInput.Password;
    }

    private void ConfirmInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is CreateVaultViewModel vm)
            vm.ConfirmPassword = ConfirmInput.Password;
    }
}
