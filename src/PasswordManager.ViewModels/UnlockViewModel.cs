using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 기존 볼트 언락 화면(마스터 비밀번호 입력) ViewModel. 마스터 비번으로 세션을 열고,
/// 성공 시 <see cref="Unlocked"/>를 발생시켜 셸이 다음 화면으로 넘어가게 한다.
/// Argon2id KDF는 CPU 집약적이라 <see cref="UnlockCommand"/>는 백그라운드에서 실행한다.
/// </summary>
public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly VaultManager _vault;

    public UnlockViewModel(VaultManager vault) => _vault = vault;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>언락 성공 시 발생. 셸이 구독해 메인 화면으로 전환한다.</summary>
    public event EventHandler? Unlocked;

    /// <summary>"비밀번호를 잊으셨나요?" 요청. 셸이 복구 화면으로 전환한다(design 5.7).</summary>
    public event EventHandler? RecoveryRequested;

    [RelayCommand]
    private void ForgotPassword() => RecoveryRequested?.Invoke(this, EventArgs.Empty);

    private bool CanUnlock() => !IsBusy && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await Task.Run(() => _vault.Open(Password));
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidMasterPasswordException)
        {
            ErrorMessage = "마스터 비밀번호가 올바르지 않습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
