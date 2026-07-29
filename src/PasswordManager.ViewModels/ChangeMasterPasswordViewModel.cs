using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 마스터 비밀번호 변경 화면 ViewModel (design 7.5, rekey). 현재 비번 확인 후 새 비번으로 교체한다.
/// KDF가 CPU 집약적이라 <see cref="ChangeCommand"/>는 백그라운드에서 실행한다.
/// </summary>
public sealed partial class ChangeMasterPasswordViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly KdfParams _kdf;

    public ChangeMasterPasswordViewModel(VaultManager vault, KdfParams? kdf = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private string _currentPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>변경 성공 시 발생. 셸이 구독해 메인으로 돌아간다.</summary>
    public event EventHandler? Changed;

    /// <summary>변경 취소 시 발생.</summary>
    public event EventHandler? Cancelled;

    private bool CanChange() =>
        !IsBusy
        && !string.IsNullOrEmpty(CurrentPassword)
        && !string.IsNullOrEmpty(NewPassword)
        && !string.IsNullOrEmpty(ConfirmPassword);

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ChangeAsync()
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
            await Task.Run(() => _vault.ChangeMasterPassword(CurrentPassword, NewPassword, _kdf));
            Changed?.Invoke(this, EventArgs.Empty);
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

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
