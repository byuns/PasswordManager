using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 언락 화면. PasswordBox는 보안상 Password를 바인딩하지 않으므로
/// 변경 시 code-behind에서 ViewModel로 전달한다.
/// </summary>
public partial class UnlockView : UserControl
{
    public UnlockView() => InitializeComponent();

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UnlockViewModel vm)
            vm.Password = PasswordInput.Password;
    }
}
