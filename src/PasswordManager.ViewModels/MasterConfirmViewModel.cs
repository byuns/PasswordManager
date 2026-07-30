using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 민감 작업(예: OTP 재설정) 전에 마스터 비밀번호를 다시 확인하는 게이트(TD-005). 이미 열린 세션
/// 앞에 앉은 제3자가 소유자 행세로 OTP를 재등록하는 것을 막는다. 확인에 성공하면 <see cref="Confirmed"/>,
/// 취소하면 <see cref="Cancelled"/>가 발생한다. 비밀번호는 볼트에 저장하지 않고 확인에만 쓴다.
/// </summary>
public sealed partial class MasterConfirmViewModel : ObservableObject
{
    private readonly VaultManager _vault;

    public MasterConfirmViewModel(VaultManager vault, string prompt)
    {
        _vault = vault;
        Prompt = prompt;
    }

    /// <summary>화면에 표시할 안내 문구(무엇을 위한 확인인지).</summary>
    public string Prompt { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>마스터 비번 확인 성공. 셸이 구독해 원래 작업을 이어간다.</summary>
    public event EventHandler? Confirmed;

    /// <summary>확인 취소. 셸이 구독해 이전 화면으로 돌아간다.</summary>
    public event EventHandler? Cancelled;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        ErrorMessage = null;
        if (_vault.VerifyMasterPassword(Password))
            Confirmed?.Invoke(this, EventArgs.Empty);
        else
            ErrorMessage = "마스터 비밀번호가 올바르지 않습니다.";
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
