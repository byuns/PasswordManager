using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 언락 화면. PasswordBox는 보안상 Password를 바인딩하지 않으므로 변경 시 code-behind에서
/// ViewModel로 전달한다. 파일 손상 시 백업 복원 대화상자도 여기서 처리한다(S9, VM은 경로만 받음).
/// </summary>
public partial class UnlockView : UserControl
{
    public UnlockView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private UnlockViewModel? Vm => DataContext as UnlockViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.RestoreRequested += OnRestoreRequested;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.RestoreRequested -= OnRestoreRequested;
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is not null)
            Vm.Password = PasswordInput.Password;
    }

    private void OnRestoreRequested(object? sender, EventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "백업에서 복원",
            Filter = "볼트 백업 (*.bak;*.dat)|*.bak;*.dat|모든 파일 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "현재(손상된) 볼트를 선택한 백업으로 덮어씁니다. 계속할까요?\n" +
            "복원 후에는 백업 시점의 마스터 비밀번호로 다시 로그인해야 합니다.",
            "볼트 복원", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        Vm!.PerformRestore(dlg.FileName);
        PasswordInput.Clear(); // VM이 Password를 비웠으므로 입력창도 비운다
    }
}
