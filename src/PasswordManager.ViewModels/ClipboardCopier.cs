using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels;

/// <summary>
/// 비밀번호를 클립보드에 복사하고 일정 시간 뒤 자동으로 비운다(design 5.5). 복사한 값이 클립보드에
/// 오래 남아 다른 앱에서 유출되는 것을 막는 위생 장치다. 클립보드·타이머를 추상화해 단위 테스트한다.
/// </summary>
public sealed class ClipboardCopier
{
    /// <summary>기본 자동 삭제 지연(design 5.5: 20초).</summary>
    public static readonly TimeSpan DefaultClearDelay = TimeSpan.FromSeconds(20);

    private readonly IClipboardService _clipboard;
    private readonly IScheduler _scheduler;

    /// <summary>복사 후 자동 삭제까지의 대기 시간. 설정에서 변경할 수 있다(design 5.5·7.9).</summary>
    public TimeSpan ClearDelay { get; set; }

    public ClipboardCopier(IClipboardService clipboard, IScheduler scheduler, TimeSpan? clearDelay = null)
    {
        _clipboard = clipboard;
        _scheduler = scheduler;
        ClearDelay = clearDelay ?? DefaultClearDelay;
    }

    /// <summary>text를 클립보드에 복사하고 <see cref="ClearDelay"/> 후 자동 삭제를 예약한다. 빈 값은 무시.</summary>
    public void CopyWithAutoClear(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _clipboard.SetText(text);
        _scheduler.Schedule(ClearDelay, _clipboard.Clear);
    }
}
