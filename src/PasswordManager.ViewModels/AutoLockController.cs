namespace PasswordManager.ViewModels;

/// <summary>
/// 유휴 시간 초과 시 자동 잠금 여부를 판단하는 순수 로직(design 5.5). 마지막 활동 시각을 기억하고,
/// 지정한 시각이 타임아웃을 넘겼는지만 계산한다. 입력 감지·주기 폴링은 UI 계층(WPF)이 담당한다.
/// </summary>
public sealed class AutoLockController
{
    private DateTimeOffset _lastActivity;

    /// <summary>유휴 허용 시간. 이 시간 동안 활동이 없으면 잠근다. 설정에서 변경할 수 있다(design 5.5·7.9).</summary>
    public TimeSpan Timeout { get; set; }

    public AutoLockController(TimeSpan timeout) => Timeout = timeout;

    /// <summary>사용자 활동(마우스·키보드 등)이 있었음을 기록해 카운트다운을 리셋한다.</summary>
    public void NotifyActivity(DateTimeOffset now) => _lastActivity = now;

    /// <summary>now 기준으로 마지막 활동 이후 타임아웃이 지났으면 true.</summary>
    public bool ShouldLock(DateTimeOffset now) => now - _lastActivity >= Timeout;
}
