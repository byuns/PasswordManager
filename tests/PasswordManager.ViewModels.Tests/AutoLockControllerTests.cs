using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class AutoLockControllerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public void Does_not_lock_before_timeout_elapses()
    {
        var c = new AutoLockController(Timeout);
        c.NotifyActivity(T0);

        Assert.False(c.ShouldLock(T0 + Timeout - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Locks_once_timeout_reached()
    {
        var c = new AutoLockController(Timeout);
        c.NotifyActivity(T0);

        Assert.True(c.ShouldLock(T0 + Timeout));
    }

    [Fact]
    public void Activity_resets_the_idle_countdown()
    {
        var c = new AutoLockController(Timeout);
        c.NotifyActivity(T0);

        // 타임아웃 직전에 활동이 있으면 카운트다운이 리셋된다.
        var almost = T0 + Timeout - TimeSpan.FromSeconds(1);
        c.NotifyActivity(almost);

        Assert.False(c.ShouldLock(almost + Timeout - TimeSpan.FromSeconds(1)));
        Assert.True(c.ShouldLock(almost + Timeout));
    }
}
