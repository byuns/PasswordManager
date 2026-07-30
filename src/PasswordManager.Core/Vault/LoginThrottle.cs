namespace PasswordManager.Core.Vault;

/// <summary>
/// 언락 재시도 제한 상태(잠긴 상태에서도 읽어야 하므로 암호화 본문 밖 별도 저장). 연속 실패 횟수와
/// 다음 재시도 허용 시각을 담는다. 성공하면 <see cref="Empty"/>로 초기화한다.
/// </summary>
public sealed record LockoutState(int FailedAttempts, DateTimeOffset? LockedUntil)
{
    public static readonly LockoutState Empty = new(0, null);
}

/// <summary>재시도 제한 상태의 영속화 추상화(파일 구현은 Storage 계층). 손상 시 <see cref="LockoutState.Empty"/>.</summary>
public interface ILockoutStore
{
    LockoutState Load();
    void Save(LockoutState state);
}

/// <summary>
/// 마스터 비밀번호 언락 재시도 제한(design 5.5·TD-024). 연속 5회 실패마다 잠그고, 잠금 시간을
/// 지수적으로 늘린다(1분 → 5분 → 30분, 이후 30분 유지). 상태를 <see cref="ILockoutStore"/>로 영속화해
/// 앱을 껐다 켜도 우회되지 않는다. 성공하면 횟수를 0으로 리셋한다.
///
/// 한계: 이 제한은 "앱 화면으로 찍어보는" 공격만 막는다. 볼트 파일을 가져간 공격자는 앱을 거치지 않고
/// 오프라인으로 대입할 수 있어 이 카운터가 무의미하다 — 그쪽 방어는 KDF(Argon2) 비용이 담당(TD-004 참고).
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>잠금을 한 단계 올리는 연속 실패 횟수.</summary>
    public const int AttemptsPerLockout = 5;

    /// <summary>단계별 잠금 시간(지수 백오프). 마지막 값에서 유지(cap).</summary>
    private static readonly TimeSpan[] Backoffs =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
    };

    private readonly ILockoutStore _store;
    private readonly Func<DateTimeOffset> _now;

    public LoginThrottle(ILockoutStore store, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>지금 잠겨 있으면 남은 시간을, 아니면 null을 돌려준다.</summary>
    public TimeSpan? RemainingLockout()
    {
        var state = _store.Load();
        if (state.LockedUntil is { } until && _now() < until)
            return until - _now();
        return null;
    }

    /// <summary>
    /// 실패 1회를 기록한다. 누적 실패가 <see cref="AttemptsPerLockout"/>의 배수가 되면 지수 백오프로
    /// 잠금 시각을 설정하고, 이번에 부과된 잠금 시간을 돌려준다(잠기지 않았으면 null).
    /// </summary>
    public TimeSpan? RecordFailure()
    {
        var state = _store.Load();
        var attempts = state.FailedAttempts + 1;
        var lockedUntil = state.LockedUntil;
        TimeSpan? imposed = null;

        if (attempts % AttemptsPerLockout == 0)
        {
            var level = attempts / AttemptsPerLockout;               // 1, 2, 3 …
            var delay = Backoffs[Math.Min(level, Backoffs.Length) - 1];
            lockedUntil = _now() + delay;
            imposed = delay;
        }

        _store.Save(new LockoutState(attempts, lockedUntil));
        return imposed;
    }

    /// <summary>언락 성공 시 호출. 실패 횟수와 잠금을 모두 초기화한다.</summary>
    public void RecordSuccess() => _store.Save(LockoutState.Empty);
}
