using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 앱 잠금해제 OTP 등록(재설정) 마법사 ViewModel (design 5.4, TD-004·TD-005).
/// secret을 메모리에서 생성해 <see cref="Secret"/>·<see cref="OtpAuthUri"/>로 노출하고,
/// 사용자가 디바이스 Authenticator에 등록한 뒤 6자리 코드를 입력하면 검증한다. 검증에 성공한
/// 경우에만 secret을 볼트에 저장한다(persist-on-confirm — 취소 시 아무것도 바뀌지 않음).
/// QR 이미지는 UI 폴리싱 단계에서 <see cref="OtpAuthUri"/>로 생성한다(TD-020).
/// </summary>
public sealed partial class OtpSetupViewModel : ObservableObject
{
    private const string Issuer = "PasswordManager";
    private const string Account = "Vault Unlock";

    private readonly VaultManager _vault;
    private readonly Func<DateTimeOffset> _now;

    public OtpSetupViewModel(VaultManager vault, Func<DateTimeOffset>? now = null)
    {
        _vault = vault;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Secret = TotpValidator.GenerateSecret();
        OtpAuthUri = OtpAuth.BuildUri(Secret, Issuer, Account);
    }

    /// <summary>디바이스에 수동 입력할 secret(Base32). 확인 전에는 저장되지 않는다.</summary>
    public string Secret { get; }

    /// <summary>디바이스 Authenticator 등록용 otpauth URI(추후 QR 이미지의 원본).</summary>
    public string OtpAuthUri { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _verificationCode = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>등록 확인·저장 성공 시 발생. 셸이 구독해 이전 화면으로 돌아간다.</summary>
    public event EventHandler? Completed;

    /// <summary>등록 취소 시 발생.</summary>
    public event EventHandler? Cancelled;

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(VerificationCode);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        ErrorMessage = null;
        if (!TotpValidator.Verify(Secret, VerificationCode, _now()))
        {
            ErrorMessage = "코드가 올바르지 않습니다. 디바이스의 6자리 코드를 다시 확인하세요.";
            return;
        }

        _vault.SetOtpSecret(Secret);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
