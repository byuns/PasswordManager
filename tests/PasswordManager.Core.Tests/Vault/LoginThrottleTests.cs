using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

public class LoginThrottleTests
{
    private sealed class MemoryStore : ILockoutStore
    {
        private LockoutState _state = LockoutState.Empty;
        public LockoutState Load() => _state;
        public void Save(LockoutState state) => _state = state;
    }

    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    [Fact]
    public void Not_locked_before_five_failures()
    {
        var throttle = new LoginThrottle(new MemoryStore(), () => T0);

        for (var i = 0; i < 4; i++)
            Assert.Null(throttle.RecordFailure()); // 1~4회는 잠금 없음
        Assert.Null(throttle.RemainingLockout());
    }

    [Fact]
    public void Fifth_failure_locks_for_one_minute()
    {
        var now = T0;
        var throttle = new LoginThrottle(new MemoryStore(), () => now);

        TimeSpan? imposed = null;
        for (var i = 0; i < 5; i++) imposed = throttle.RecordFailure();

        Assert.Equal(TimeSpan.FromMinutes(1), imposed);
        Assert.Equal(TimeSpan.FromMinutes(1), throttle.RemainingLockout());
    }

    [Fact]
    public void Backoff_escalates_1_5_30_minutes_and_caps()
    {
        var now = T0;
        var throttle = new LoginThrottle(new MemoryStore(), () => now);

        TimeSpan? Fail5()
        {
            TimeSpan? last = null;
            for (var i = 0; i < 5; i++) last = throttle.RecordFailure();
            now += TimeSpan.FromHours(1); // 잠금 만료시켜 다음 5회 시도 가능하게
            return last;
        }

        Assert.Equal(TimeSpan.FromMinutes(1), Fail5());   // 5회
        Assert.Equal(TimeSpan.FromMinutes(5), Fail5());   // 10회
        Assert.Equal(TimeSpan.FromMinutes(30), Fail5());  // 15회
        Assert.Equal(TimeSpan.FromMinutes(30), Fail5());  // 20회 — 상한 유지
    }

    [Fact]
    public void Lockout_clears_after_time_passes()
    {
        var now = T0;
        var throttle = new LoginThrottle(new MemoryStore(), () => now);
        for (var i = 0; i < 5; i++) throttle.RecordFailure();
        Assert.NotNull(throttle.RemainingLockout());

        now += TimeSpan.FromMinutes(1);

        Assert.Null(throttle.RemainingLockout()); // 1분 지나면 재시도 가능
    }

    [Fact]
    public void Success_resets_attempts_and_lock()
    {
        var now = T0;
        var store = new MemoryStore();
        var throttle = new LoginThrottle(store, () => now);
        for (var i = 0; i < 5; i++) throttle.RecordFailure();

        throttle.RecordSuccess();

        Assert.Equal(LockoutState.Empty, store.Load());
        Assert.Null(throttle.RemainingLockout());
    }

    [Fact]
    public void State_persists_across_instances_via_store()
    {
        var now = T0;
        var store = new MemoryStore();
        for (var i = 0; i < 5; i++) new LoginThrottle(store, () => now).RecordFailure();

        // 새 인스턴스(앱 재시작 시나리오)도 같은 저장소를 읽어 잠금이 유지된다
        var reopened = new LoginThrottle(store, () => now);
        Assert.Equal(TimeSpan.FromMinutes(1), reopened.RemainingLockout());
    }
}
