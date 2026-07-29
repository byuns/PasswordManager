using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 마스터 비밀번호 분실 복구 화면 ViewModel (design 5.7). 복구 키와 새 마스터 비밀번호를 받아
/// 마스터 비번을 재설정하고 세션을 연다. 성공 시 <see cref="Recovered"/>를 발생시킨다.
/// KDF가 CPU 집약적이라 <see cref="RecoverCommand"/>는 백그라운드에서 실행한다.
/// </summary>
public sealed partial class RecoveryViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly KdfParams _kdf;

    public RecoveryViewModel(VaultManager vault, KdfParams? kdf = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private string _recoveryCodeInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>복구·재설정 성공 시 발생. 셸이 구독해 메인으로 진입한다.</summary>
    public event EventHandler? Recovered;

    /// <summary>복구 취소 시 발생. 셸이 언락 화면으로 되돌린다.</summary>
    public event EventHandler? Cancelled;

    private bool CanRecover() =>
        !IsBusy
        && !string.IsNullOrEmpty(RecoveryCodeInput)
        && !string.IsNullOrEmpty(NewPassword)
        && !string.IsNullOrEmpty(ConfirmPassword);

    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task RecoverAsync()
    {
        ErrorMessage = null;
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "두 비밀번호가 일치하지 않습니다.";
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _vault.Recover(RecoveryCodeInput, NewPassword, _kdf));
            Recovered?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is InvalidRecoveryKeyException or FormatException)
        {
            ErrorMessage = "복구 키가 올바르지 않습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
