using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 복구 키 재발급 화면 ViewModel (design 7.6). 현재 마스터 비밀번호를 확인해 새 복구 키로 교체하고,
/// 최초 1회 <see cref="RecoveryKeyDisplay"/>에 노출한다(이전 복구 키는 무효화). 마스터 변경 화면과 대칭이며
/// 복구 키 표시·보관 확인은 최초 설정(<see cref="CreateVaultViewModel"/>)과 같은 방식이다.
/// Argon2id KDF 확인이 CPU 집약적이라 <see cref="ReissueCommand"/>는 백그라운드에서 실행한다.
/// </summary>
public sealed partial class ReissueRecoveryKeyViewModel : ObservableObject
{
    private readonly VaultManager _vault;

    // kdf는 복구 래핑만 교체하는 재발급에선 쓰이지 않지만, 다른 화면들과 생성 시그니처를 맞춰 받는다.
    public ReissueRecoveryKeyViewModel(VaultManager vault, KdfParams? kdf = null)
    {
        _vault = vault;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReissueCommand))]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReissueCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>재발급 성공 시 최초 1회 표시할 새 복구 키(그룹 형식). 발급 전에는 null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReissueCommand))]
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))]
    private string? _recoveryKeyDisplay;

    /// <summary>사용자가 새 복구 키를 안전하게 보관했음을 확인(체크)했는가 (design 7.6).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))]
    private bool _recoveryKeySaved;

    /// <summary>새 복구 키 보관을 확인하고 완료할 때 발생. 셸이 구독해 설정으로 돌아간다.</summary>
    public event EventHandler? Completed;

    /// <summary>재발급을 취소할 때 발생.</summary>
    public event EventHandler? Cancelled;

    private bool CanReissue() =>
        !IsBusy && RecoveryKeyDisplay is null && !string.IsNullOrEmpty(CurrentPassword);

    [RelayCommand(CanExecute = nameof(CanReissue))]
    private async Task ReissueAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            RecoveryKeyDisplay = await Task.Run(() => _vault.ReissueRecoveryKey(CurrentPassword));
        }
        catch (InvalidMasterPasswordException)
        {
            ErrorMessage = "현재 마스터 비밀번호가 올바르지 않습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAcknowledge() => RecoveryKeyDisplay is not null && RecoveryKeySaved;

    [RelayCommand(CanExecute = nameof(CanAcknowledge))]
    private void Acknowledge() => Completed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
