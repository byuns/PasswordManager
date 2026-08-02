using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;
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
            shell.Dialog ??= new WpfUiDialogService(ShowConfirmAsync, ShowPromptAsync, RootSnackbar);
    }

    private TaskCompletionSource<bool>? _confirmTcs;
    private TaskCompletionSource<string?>? _promptTcs;

    /// <summary>커스텀 확인 오버레이를 띄우고 사용자의 선택(확인=true/취소=false)을 비동기로 돌려준다.</summary>
    private Task<bool> ShowConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        DismissPending();

        ConfirmTitle.Text = title;
        ConfirmMessage.Text = message;
        ConfirmOkButton.Content = confirmText;
        ConfirmOkButton.Appearance = ControlAppearance.Danger;
        ConfirmCancelButton.Content = cancelText;
        ConfirmInput.Visibility = Visibility.Collapsed;

        _confirmTcs = new TaskCompletionSource<bool>();
        ConfirmOverlay.Visibility = Visibility.Visible;
        // 방금 Visible이 된 직후 동기 Focus는 실패할 수 있어 레이아웃 후로 미룬다.
        // 파괴적 동작이므로 기본 포커스는 취소 버튼에 둔다(포커스 트랩은 XAML의 KeyboardNavigation=Cycle).
        Dispatcher.BeginInvoke(new Action(() => ConfirmCancelButton.Focus()), DispatcherPriority.Input);
        return _confirmTcs.Task;
    }

    /// <summary>
    /// 같은 오버레이에 한 줄 입력창을 띄우고 입력값을 돌려준다(취소=null). 복구 키처럼 그 자리에서
    /// 받아 쓰고 버리는 값에 쓴다(TD-050). 파괴적 동작이 아니므로 기본 포커스는 입력창에 둔다.
    /// </summary>
    private Task<string?> ShowPromptAsync(
        string title, string message, string placeholder, string confirmText, string cancelText)
    {
        DismissPending();

        ConfirmTitle.Text = title;
        ConfirmMessage.Text = message;
        ConfirmOkButton.Content = confirmText;
        ConfirmOkButton.Appearance = ControlAppearance.Primary;
        ConfirmCancelButton.Content = cancelText;
        ConfirmInput.Text = string.Empty;
        ConfirmInput.PlaceholderText = placeholder;
        ConfirmInput.Visibility = Visibility.Visible;

        _promptTcs = new TaskCompletionSource<string?>();
        ConfirmOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => ConfirmInput.Focus()), DispatcherPriority.Input);
        return _promptTcs.Task;
    }

    /// <summary>앞선 확인·입력이 떠 있으면 취소로 정리한다(중복 표시 방지).</summary>
    private void DismissPending()
    {
        _confirmTcs?.TrySetResult(false);
        _confirmTcs = null;
        _promptTcs?.TrySetResult(null);
        _promptTcs = null;
    }

    private void OnConfirmOk(object sender, RoutedEventArgs e) => CloseConfirm(true);

    private void OnConfirmCancel(object sender, RoutedEventArgs e) => CloseConfirm(false);

    private void CloseConfirm(bool result)
    {
        var typed = ConfirmInput.Text;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        ConfirmInput.Visibility = Visibility.Collapsed;
        ConfirmInput.Text = string.Empty; // 입력한 복구 키를 창에 남겨두지 않는다

        var confirm = _confirmTcs;
        var prompt = _promptTcs;
        _confirmTcs = null;
        _promptTcs = null;
        confirm?.TrySetResult(result);
        prompt?.TrySetResult(result ? typed : null);
    }

    /// <summary>본문(CurrentViewModel) 전환 시 짧게 fade-in 한다. 뷰 재사용 여부와 무관하게
    /// 바인딩 갱신(TargetUpdated)마다 재생돼, 섹션 이동·흐름 전환이 부드럽게 이어진다.</summary>
    private void MainContent_TargetUpdated(object sender, DataTransferEventArgs e) =>
        MainContent.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.18))));

    private void NotifyActivity() => _autoLock.NotifyActivity(DateTimeOffset.UtcNow);
}
