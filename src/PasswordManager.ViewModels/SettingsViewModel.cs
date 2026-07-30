using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 설정 화면 ViewModel. 상단 툴바에 몰려 있던 보조 동작(OTP 등록/재설정 · 마스터 변경 ·
/// 백업/복원)을 이곳으로 모은다(design-ux 3절, S7). 실제 하위 화면 전환·파일 대화상자는
/// 이벤트로 셸/뷰에 위임하고, 여기서는 볼트 조작(백업/복원)만 직접 수행한다.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly VaultManager _vault;

    public SettingsViewModel(VaultManager vault) => _vault = vault;

    /// <summary>사용자에게 보여줄 일시 안내(예: 백업 완료).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>앱 잠금해제 OTP가 등록되어 있는가(상태 표시·재설정 안내용).</summary>
    public bool IsOtpRegistered => _vault.HasOtp;

    /// <summary>OTP 등록 상태를 사람이 읽는 문자열로(설정 카드 표시용).</summary>
    public string OtpStatusText => IsOtpRegistered ? "등록됨" : "미등록";

    /// <summary>OTP 등록(재설정) 요청. 셸이 등록 마법사를 연다.</summary>
    public event EventHandler? OtpSetupRequested;

    /// <summary>마스터 비밀번호 변경 요청. 셸이 변경 화면을 연다.</summary>
    public event EventHandler? ChangeMasterRequested;

    /// <summary>백업 요청. 뷰가 저장 위치 대화상자를 연다.</summary>
    public event EventHandler? BackupRequested;

    /// <summary>복원 요청. 뷰가 백업 파일 선택 대화상자를 연다.</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>복원으로 세션이 닫혔을 때 발생. 셸이 언락 화면으로 돌아간다.</summary>
    public event EventHandler? Locked;

    /// <summary>OTP 등록 상태 등 볼트에서 읽는 값을 다시 반영한다(하위 화면에서 복귀 시).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOtpRegistered));
        OnPropertyChanged(nameof(OtpStatusText));
    }

    [RelayCommand]
    private void SetupOtp() => OtpSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChangeMasterPassword() => ChangeMasterRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Backup() => BackupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>선택한 경로로 볼트를 백업한다(뷰가 대화상자에서 경로를 받아 호출).</summary>
    public void PerformBackup(string path)
    {
        _vault.Backup(path);
        StatusMessage = "백업을 완료했습니다.";
    }

    /// <summary>백업 파일로 복원하고 잠금 화면으로 돌아간다(백업 시점의 마스터 비번으로 재로그인).</summary>
    public void PerformRestore(string path)
    {
        _vault.Restore(path);                  // 세션 닫힘
        Locked?.Invoke(this, EventArgs.Empty); // 언락 화면으로 전환
    }
}
