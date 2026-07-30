using PasswordManager.ViewModels;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels.Tests;

public class ClipboardCopierTests
{
    /// <summary>클립보드 호출을 기록하는 가짜.</summary>
    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public int ClearCount { get; private set; }
        public void SetText(string text) => Text = text;
        public void Clear() { Text = null; ClearCount++; }
    }

    /// <summary>예약된 (지연, 동작)을 붙잡아 테스트가 수동으로 실행하는 가짜 스케줄러.</summary>
    private sealed class ManualScheduler : IScheduler
    {
        public TimeSpan LastDelay { get; private set; }
        private Action? _pending;
        public void Schedule(TimeSpan delay, Action action) { LastDelay = delay; _pending = action; }
        public void RunPending() { _pending?.Invoke(); }
    }

    [Fact]
    public void Plain_copy_sets_text_without_scheduling_clear()
    {
        var clip = new FakeClipboard();
        var sched = new ManualScheduler();

        new ClipboardCopier(clip, sched).Copy("user@example.com");

        Assert.Equal("user@example.com", clip.Text);
        Assert.Equal(TimeSpan.Zero, sched.LastDelay); // 아무 예약도 안 함
    }

    [Fact]
    public void Copy_sets_text_and_schedules_clear_after_20_seconds()
    {
        var clip = new FakeClipboard();
        var sched = new ManualScheduler();
        var copier = new ClipboardCopier(clip, sched);

        copier.CopyWithAutoClear("s3cr3t");

        Assert.Equal("s3cr3t", clip.Text);
        Assert.Equal(TimeSpan.FromSeconds(20), sched.LastDelay); // design 5.5
        Assert.Equal(0, clip.ClearCount);                        // 아직 안 지움
    }

    [Fact]
    public void Scheduled_action_clears_the_clipboard()
    {
        var clip = new FakeClipboard();
        var sched = new ManualScheduler();
        new ClipboardCopier(clip, sched).CopyWithAutoClear("s3cr3t");

        sched.RunPending(); // 20초 경과 시뮬레이션

        Assert.Null(clip.Text);
        Assert.Equal(1, clip.ClearCount);
    }

    [Fact]
    public void Copy_ignores_null_or_empty_text()
    {
        var clip = new FakeClipboard();
        var sched = new ManualScheduler();
        var copier = new ClipboardCopier(clip, sched);

        copier.CopyWithAutoClear(null);
        copier.CopyWithAutoClear("");

        Assert.Null(clip.Text);
        Assert.Equal(TimeSpan.Zero, sched.LastDelay); // 예약도 안 함
    }

    [Fact]
    public void Custom_clear_delay_is_respected()
    {
        var sched = new ManualScheduler();
        var copier = new ClipboardCopier(new FakeClipboard(), sched, TimeSpan.FromSeconds(5));

        copier.CopyWithAutoClear("x");

        Assert.Equal(TimeSpan.FromSeconds(5), sched.LastDelay);
    }
}
