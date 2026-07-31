using PasswordManager.Core.Notifications;

namespace PasswordManager.Core.Tests.Notifications;

public class FileSlackFailureLogTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 31, 14, 3, 0, TimeSpan.FromHours(9));

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "pwm-slacklog-" + Guid.NewGuid().ToString("N") + ".log");

    [Fact]
    public void Records_event_kind_and_time_as_one_line()
    {
        var path = TempPath();
        try
        {
            new FileSlackFailureLog(path).RecordFailure(SlackEvent.Unlock, At);

            var lines = File.ReadAllLines(path);
            Assert.Single(lines);
            Assert.Contains("Unlock", lines[0]);
            Assert.Contains(At.ToString("o"), lines[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Appends_multiple_failures_in_order()
    {
        var path = TempPath();
        try
        {
            var log = new FileSlackFailureLog(path);
            log.RecordFailure(SlackEvent.Unlock, At);
            log.RecordFailure(SlackEvent.LoginFailure, At);

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Contains("Unlock", lines[0]);
            Assert.Contains("LoginFailure", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Creates_missing_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pwm-slacklog-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "failures.log");
        try
        {
            new FileSlackFailureLog(path).RecordFailure(SlackEvent.LoginFailure, At);
            Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Trims_to_stay_within_size_cap_keeping_recent_lines()
    {
        var path = TempPath();
        try
        {
            var log = new FileSlackFailureLog(path, maxBytes: 300);
            for (var i = 0; i < 200; i++)
                log.RecordFailure(SlackEvent.LoginFailure, At); // 상한을 크게 초과하도록 반복

            // 매 기록 종료 시점엔 상한 이하로 억제된다(트림 후 최근 절반만 유지).
            Assert.True(new FileInfo(path).Length <= 300, $"file grew to {new FileInfo(path).Length}B");

            // 오래된 게 잘려도 최근 실패 줄은 남아 있어야 한다.
            var lines = File.ReadAllLines(path);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.Contains("LoginFailure", l));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Does_not_write_secret_like_url()
    {
        // RecordFailure는 종류·시각만 받는다 → 파일에 URL 조각이 절대 섞이지 않아야 한다.
        var path = TempPath();
        try
        {
            new FileSlackFailureLog(path).RecordFailure(SlackEvent.PasswordChange, At);

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hooks.slack.com", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }
}
