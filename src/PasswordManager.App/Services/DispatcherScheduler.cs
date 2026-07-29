using System.Windows.Threading;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.App.Services;

/// <summary>UI 스레드의 <see cref="DispatcherTimer"/>로 지연 실행을 구현한 <see cref="IScheduler"/>.
/// 한 번 실행 후 스스로 멈춘다(클립보드 자동 삭제 등에 사용).</summary>
public sealed class DispatcherScheduler : IScheduler
{
    public void Schedule(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
