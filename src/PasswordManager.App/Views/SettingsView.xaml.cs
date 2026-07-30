using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 설정 화면. 보조 동작(OTP·마스터 변경·백업·복원)을 모은다(design-ux 3절). 대부분 로직은
/// SettingsViewModel에 있고, 백업/복원 파일 대화상자만 여기서 처리한다(VM은 경로만 전달받음).
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.BackupRequested += OnBackupRequested;
        Vm.RestoreRequested += OnRestoreRequested;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.BackupRequested -= OnBackupRequested;
        Vm.RestoreRequested -= OnRestoreRequested;
    }

    private void OnBackupRequested(object? sender, EventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "볼트 백업",
            FileName = "vault-backup.dat",
            Filter = "볼트 백업 (*.dat)|*.dat|모든 파일 (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            Vm!.PerformBackup(dlg.FileName);
    }

    private void OnRestoreRequested(object? sender, EventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "볼트 복원",
            Filter = "볼트 백업 (*.dat)|*.dat|모든 파일 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "현재 볼트를 선택한 백업으로 덮어씁니다. 계속할까요?\n" +
            "복원 후에는 백업 시점의 마스터 비밀번호로 다시 로그인해야 합니다.",
            "볼트 복원", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.OK)
            Vm!.PerformRestore(dlg.FileName);
    }
}
