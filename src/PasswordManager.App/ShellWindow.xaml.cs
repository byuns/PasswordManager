using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PasswordManager.App.Services;
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
            if (DataContext is not ShellViewModel shell) return;
            _autoLock.Timeout = shell.AutoLockTimeout; // 볼트 설정값을 매 폴링마다 반영
            if (_autoLock.ShouldLock(DateTimeOffset.UtcNow))
                shell.AutoLock();
        };
        _poll.Start();

        // 창의 팝업 호스트가 준비된 뒤 다이얼로그 서비스를 셸에 주입한다(확인창·완료 토스트).
        Loaded += OnShellLoaded;
    }

    private void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
            shell.Dialog ??= new WpfUiDialogService(ShowConfirmAsync, RootSnackbar);
    }

    private TaskCompletionSource<bool>? _confirmTcs;

    /// <summary>커스텀 확인 오버레이를 띄우고 사용자의 선택(확인=true/취소=false)을 비동기로 돌려준다.</summary>
    private Task<bool> ShowConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        // 앞선 확인이 남아 있으면 취소로 정리한다(중복 표시 방지).
        _confirmTcs?.TrySetResult(false);

        ConfirmTitle.Text = title;
        ConfirmMessage.Text = message;
        ConfirmOkButton.Content = confirmText;
        ConfirmCancelButton.Content = cancelText;

        _confirmTcs = new TaskCompletionSource<bool>();
        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmCancelButton.Focus(); // 파괴적 동작이므로 기본 포커스는 취소에 둔다
        return _confirmTcs.Task;
    }

    private void OnConfirmOk(object sender, RoutedEventArgs e) => CloseConfirm(true);

    private void OnConfirmCancel(object sender, RoutedEventArgs e) => CloseConfirm(false);

    private void CloseConfirm(bool result)
    {
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        var tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }

    private void NotifyActivity() => _autoLock.NotifyActivity(DateTimeOffset.UtcNow);
}
