using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 최초 실행 시 새 볼트 생성 화면 ViewModel (design 5.7, 7.6). 마스터 비밀번호를 설정·확인하고
/// 볼트를 만든 뒤, 최초 1회 보여줄 복구 키를 <see cref="RecoveryKeyDisplay"/>에 노출한다(TD-010).
/// Argon2id KDF는 CPU 집약적이라 <see cref="CreateCommand"/>는 백그라운드에서 실행한다.
/// </summary>
public sealed partial class CreateVaultViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly KdfParams _kdf;

    public CreateVaultViewModel(VaultManager vault, KdfParams? kdf = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>생성 성공 시 최초 1회 표시할 복구 키(그룹 형식). 생성 전에는 null.</summary>
    [ObservableProperty]
    private string? _recoveryKeyDisplay;

    /// <summary>볼트 생성·세션 오픈 성공 시 발생. 셸이 구독해 복구 키 확인 화면으로 넘어간다.</summary>
    public event EventHandler? Created;

    private bool CanCreate() =>
        !IsBusy && !string.IsNullOrEmpty(Password) && !string.IsNullOrEmpty(ConfirmPassword);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        ErrorMessage = null;
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "두 비밀번호가 일치하지 않습니다.";
            return;
        }

        IsBusy = true;
        try
        {
            var recoveryKey = await Task.Run(() => _vault.CreateNew(Password, _kdf));
            RecoveryKeyDisplay = RecoveryCode.Encode(recoveryKey);
            Created?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
