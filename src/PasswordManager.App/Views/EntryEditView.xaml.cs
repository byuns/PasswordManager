using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 항목 추가/편집 폼. 비밀번호는 로드 시 프리필하고 변경 시 ViewModel로 전달한다.
/// 생성기로 ViewModel.Password가 바뀌면(코드 경로) PasswordBox에도 반영한다.
/// </summary>
public partial class EntryEditView : UserControl
{
    private EntryEditViewModel? _vm;

    public EntryEditView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryEditViewModel vm)
            return;

        _vm = vm;
        PasswordInput.Password = vm.Password;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) => vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntryEditViewModel.Password) &&
            _vm is not null && PasswordInput.Password != _vm.Password)
        {
            PasswordInput.Password = _vm.Password;
        }
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is EntryEditViewModel vm)
            vm.Password = PasswordInput.Password;
    }
}
