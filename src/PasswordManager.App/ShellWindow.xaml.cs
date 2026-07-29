using System;
using System.Windows.Threading;
using PasswordManager.ViewModels;
using Wpf.Ui.Controls;

namespace PasswordManager.App;

/// <summary>
/// 앱의 최상위 창. DataContext(ShellViewModel)의 CurrentViewModel을 DataTemplate로 렌더링한다.
/// 마우스·키보드 입력으로 활동을 감지하고 주기적으로 폴링해 유휴 시간 초과 시 자동 잠금한다(design 5.5).
/// </summary>
public partial class ShellWindow : FluentWindow
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private readonly AutoLockController _autoLock = new(IdleTimeout);
    private readonly DispatcherTimer _poll;

    public ShellWindow()
    {
        InitializeComponent();
        _autoLock.NotifyActivity(DateTimeOffset.UtcNow);

        // 활동 감지: 어떤 입력이든 유휴 카운트다운을 리셋한다.
        PreviewMouseDown += (_, _) => NotifyActivity();
        PreviewMouseMove += (_, _) => NotifyActivity();
        PreviewKeyDown += (_, _) => NotifyActivity();

        // 주기 폴링: 타임아웃을 넘겼으면 셸에 자동 잠금을 요청한다.
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _poll.Tick += (_, _) =>
        {
            if (_autoLock.ShouldLock(DateTimeOffset.UtcNow) && DataContext is ShellViewModel shell)
                shell.AutoLock();
        };
        _poll.Start();
    }

    private void NotifyActivity() => _autoLock.NotifyActivity(DateTimeOffset.UtcNow);
}
