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
    private readonly LoginThrottle? _throttle;

    public UnlockViewModel(VaultManager vault, LoginThrottle? throttle = null)
    {
        _vault = vault;
        _throttle = throttle;
    }

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

        // 재시도 제한이 걸려 있으면 비번을 확인하지 않고 남은 시간을 안내한다(TD-024).
        if (_throttle?.RemainingLockout() is { } remaining)
        {
            ErrorMessage = $"너무 많이 시도했습니다. {FormatDuration(remaining)} 후 다시 시도하세요.";
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _vault.Open(Password));
            _throttle?.RecordSuccess();
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidMasterPasswordException)
        {
            var imposed = _throttle?.RecordFailure();
            ErrorMessage = imposed is { } locked
                ? $"마스터 비밀번호를 여러 번 틀렸습니다. {FormatDuration(locked)} 동안 잠깁니다."
                : "마스터 비밀번호가 올바르지 않습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>남은/잠금 시간을 사람이 읽는 한국어로("2분 30초", "5분", "45초").</summary>
    private static string FormatDuration(TimeSpan span)
    {
        var total = (int)Math.Ceiling(Math.Max(0, span.TotalSeconds));
        int minutes = total / 60, seconds = total % 60;
        if (minutes > 0 && seconds > 0) return $"{minutes}분 {seconds}초";
        return minutes > 0 ? $"{minutes}분" : $"{seconds}초";
    }
}
