using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>항목 추가/편집 폼. 비밀번호는 로드 시 프리필하고 변경 시 ViewModel로 전달한다.</summary>
public partial class EntryEditView : UserControl
{
    public EntryEditView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryEditViewModel vm)
            PasswordInput.Password = vm.Password;
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryEditViewModel vm)
            vm.Password = PasswordInput.Password;
    }
}
