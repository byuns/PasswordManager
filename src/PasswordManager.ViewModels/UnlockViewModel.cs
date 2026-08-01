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

    /// <summary>파일 손상이 감지된 상태(비번 오류와 구분). 이때만 "백업에서 복원" CTA를 노출한다(S9).</summary>
    [ObservableProperty]
    private bool _isCorrupted;

    /// <summary>언락 성공 시 발생. 셸이 구독해 메인 화면으로 전환한다.</summary>
    public event EventHandler? Unlocked;

    /// <summary>마스터 비밀번호가 틀려 언락에 실패했을 때 발생(파일 손상·재시도 잠금은 제외). 셸이 슬랙 알림에 쓴다(design 7.8).</summary>
    public event EventHandler? LoginFailed;

    /// <summary>"비밀번호를 잊으셨나요?" 요청. 셸이 복구 화면으로 전환한다(design 5.7).</summary>
    public event EventHandler? RecoveryRequested;

    /// <summary>"백업에서 복원" 요청. 뷰가 백업 파일 선택 대화상자를 연다(S9·TD-028).</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>전체 초기화 요청(<see cref="ResetCommand"/> 입력). 셸이 경고창을 띄운다(TD-044).</summary>
    public event EventHandler? ResetRequested;

    /// <summary>
    /// 비밀번호 칸에 입력하면 전체 초기화로 빠지는 커맨드(TD-044). 화면에 버튼을 두지 않아
    /// 실수로 누를 수 없고, 마스터 비밀번호 최소 길이(12자)보다 짧아 실제 비번과 충돌하지 않는다.
    /// </summary>
    public const string ResetCommand = "/reset";

    [RelayCommand]
    private void ForgotPassword() => RecoveryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>백업 파일로 볼트를 복원한다(뷰가 대화상자에서 경로를 받아 호출). 복원 후에는
    /// 백업 시점의 마스터 비밀번호로 다시 열어야 하므로 손상 표시·오류·입력을 비운다.</summary>
    public void PerformRestore(string backupPath)
    {
        _vault.Restore(backupPath);
        IsCorrupted = false;
        ErrorMessage = null;
        Password = string.Empty;
    }

    private bool CanUnlock() => !IsBusy && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;
        IsCorrupted = false;

        // 초기화 커맨드는 비밀번호가 아니다 → 재시도 제한보다 먼저 확인한다.
        // 비번을 여러 번 틀려 잠긴 상태야말로 초기화가 필요한 순간이라, 여기서 막히면 안 된다(TD-044).
        if (string.Equals(Password.Trim(), ResetCommand, StringComparison.OrdinalIgnoreCase))
        {
            Password = string.Empty; // 커맨드가 입력란에 남지 않게
            ResetRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

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
            LoginFailed?.Invoke(this, EventArgs.Empty);
        }
        catch (VaultCorruptedException)
        {
            // 비번은 맞지만 파일 본문 인증이 실패한 경우 = 파일 손상(design 5.3·12). 비번 시도 실패가
            // 아니므로 재시도 제한을 올리지 않고, 비번 오류와 구분되는 복구 안내 + 복원 CTA를 노출한다(S9).
            ErrorMessage = "볼트 파일이 손상되어 열 수 없습니다. 자동 백업(vault.dat.bak)에서 복원해 보세요.";
            IsCorrupted = true;
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
