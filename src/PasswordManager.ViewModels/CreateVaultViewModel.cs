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
    private readonly ClipboardCopier? _copier;

    public CreateVaultViewModel(VaultManager vault, KdfParams? kdf = null, ClipboardCopier? copier = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;
        _copier = copier;
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
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyRecoveryKeyCommand))]
    private string? _recoveryKeyDisplay;

    /// <summary>사용자가 복구 키를 안전하게 보관했음을 확인(체크)했는가 (design 7.6).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcknowledgeCommand))]
    private bool _recoveryKeySaved;

    /// <summary>볼트 생성·세션 오픈 성공 시 발생. 셸이 구독해 복구 키 확인 화면으로 넘어간다.</summary>
    public event EventHandler? Created;

    /// <summary>복구 키 보관을 확인하고 다음(메인)으로 진행할 때 발생.</summary>
    public event EventHandler? Completed;

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

    private bool CanAcknowledge() => RecoveryKeyDisplay is not null && RecoveryKeySaved;

    [RelayCommand(CanExecute = nameof(CanAcknowledge))]
    private void Acknowledge() => Completed?.Invoke(this, EventArgs.Empty);

    private bool CanCopyRecoveryKey() => RecoveryKeyDisplay is not null;

    /// <summary>
    /// 복구 키를 클립보드로 복사한다(TD-043). 복구 키는 볼트를 여는 비밀이므로 아이디와 달리
    /// <see cref="ClipboardCopier.CopyWithAutoClear"/>로 자동 삭제를 건다(design 5.5).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyRecoveryKey))]
    private void CopyRecoveryKey() => _copier?.CopyWithAutoClear(RecoveryKeyDisplay);
}
