using PasswordManager.Core.Models;
using PasswordManager.Core.Notifications;

namespace PasswordManager.Core.Tests.Notifications;

public class SlackNotifierTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 31, 14, 3, 0, TimeSpan.FromHours(9));
    private const string Url = "https://hooks.slack.com/services/T/B/X";

    /// <summary>POST 호출을 기록하는 가짜 Webhook 클라이언트. 성공/실패·예외를 주입할 수 있다.</summary>
    private sealed class FakeWebhookClient : IWebhookClient
    {
        private readonly Queue<Func<bool>> _responses;
        public List<(string Url, string Payload)> Calls { get; } = new();

        public FakeWebhookClient(params Func<bool>[] responses) => _responses = new(responses);

        public Task<bool> PostAsync(string url, string jsonPayload, CancellationToken ct)
        {
            Calls.Add((url, jsonPayload));
            var next = _responses.Count > 0 ? _responses.Dequeue() : (() => true);
            return Task.FromResult(next());
        }
    }

    private sealed class RecordingLog : ISlackFailureLog
    {
        public List<SlackEvent> Failures { get; } = new();
        public void RecordFailure(SlackEvent kind, DateTimeOffset time) => Failures.Add(kind);
    }

    private static SlackSettings On() => new()
    {
        Enabled = true,
        WebhookUrl = Url,
        NotifyUnlock = true,
        NotifyLoginFailure = true,
        NotifyPasswordChange = true,
        NotifySensitive = true,
    };

    private static SlackNotifier Notifier(FakeWebhookClient client, bool network, SlackSettings slack,
        ISlackFailureLog? log = null)
        => new(() => new SlackConfig(network, slack), client, log);

    [Fact]
    public async Task Does_not_post_when_network_blocked()
    {
        var client = new FakeWebhookClient();
        await Notifier(client, network: false, On()).NotifyAsync(SlackEvent.Unlock, At);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Does_not_post_when_slack_disabled()
    {
        var client = new FakeWebhookClient();
        var s = On(); s.Enabled = false;
        await Notifier(client, true, s).NotifyAsync(SlackEvent.Unlock, At);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Does_not_post_when_event_toggle_off()
    {
        var client = new FakeWebhookClient();
        var s = On(); s.NotifyUnlock = false;
        await Notifier(client, true, s).NotifyAsync(SlackEvent.Unlock, At);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Does_not_post_when_webhook_url_empty()
    {
        var client = new FakeWebhookClient();
        var s = On(); s.WebhookUrl = "  ";
        await Notifier(client, true, s).NotifyAsync(SlackEvent.Unlock, At);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Does_not_post_when_vault_locked()
    {
        var client = new FakeWebhookClient();
        var n = new SlackNotifier(() => null, client); // provider null = 잠김
        await n.NotifyAsync(SlackEvent.Unlock, At);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Posts_rendered_message_to_webhook_url()
    {
        var client = new FakeWebhookClient();
        var s = On(); s.MessageTemplate = "🔓 {이벤트} · {시각}";
        await Notifier(client, true, s).NotifyAsync(SlackEvent.Unlock, At);

        var call = Assert.Single(client.Calls);
        Assert.Equal(Url, call.Url);
        // 페이로드를 파싱해 text 필드의 의미를 검증(JSON 이스케이프 방식과 무관).
        using var doc = System.Text.Json.JsonDocument.Parse(call.Payload);
        Assert.Equal("🔓 잠금 해제됨 · 2026-07-31 14:03", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Sensitive_event_uses_sensitive_toggle()
    {
        var client = new FakeWebhookClient();
        var s = On(); s.NotifySensitive = false;
        await Notifier(client, true, s).NotifyAsync(SlackEvent.MasterPasswordChanged, At);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Includes_site_name_only_when_enabled()
    {
        var withSite = new FakeWebhookClient();
        var s1 = On(); s1.IncludeSiteName = true; s1.MessageTemplate = "{이벤트}";
        await Notifier(withSite, true, s1).NotifyAsync(SlackEvent.PasswordChange, At, siteName: "Steam");
        Assert.Contains("Steam", withSite.Calls[0].Payload);

        var noSite = new FakeWebhookClient();
        var s2 = On(); s2.IncludeSiteName = false; s2.MessageTemplate = "{이벤트}";
        await Notifier(noSite, true, s2).NotifyAsync(SlackEvent.PasswordChange, At, siteName: "Steam");
        Assert.DoesNotContain("Steam", noSite.Calls[0].Payload);
    }

    [Fact]
    public async Task Retries_once_then_succeeds_without_logging_failure()
    {
        var client = new FakeWebhookClient(() => false, () => true); // 1차 실패, 2차 성공
        var log = new RecordingLog();
        await Notifier(client, true, On(), log).NotifyAsync(SlackEvent.Unlock, At);

        Assert.Equal(2, client.Calls.Count);
        Assert.Empty(log.Failures);
    }

    [Fact]
    public async Task Both_attempts_fail_logs_failure_and_does_not_throw()
    {
        var client = new FakeWebhookClient(() => false, () => false);
        var log = new RecordingLog();

        await Notifier(client, true, On(), log).NotifyAsync(SlackEvent.Unlock, At);

        Assert.Equal(2, client.Calls.Count);
        Assert.Equal(SlackEvent.Unlock, Assert.Single(log.Failures));
    }

    [Fact]
    public async Task Client_exception_is_swallowed_and_retried()
    {
        var client = new ThrowingClient(throwTimes: 2);
        var log = new RecordingLog();

        // 예외가 밖으로 새면 이 await에서 터진다 → 베스트에포트 위반.
        await new SlackNotifier(() => new SlackConfig(true, On()), client, log)
            .NotifyAsync(SlackEvent.Unlock, At);

        Assert.Equal(2, client.Attempts);
        Assert.Single(log.Failures);
    }

    private sealed class ThrowingClient : IWebhookClient
    {
        private int _remaining;
        public int Attempts { get; private set; }
        public ThrowingClient(int throwTimes) => _remaining = throwTimes;
        public Task<bool> PostAsync(string url, string jsonPayload, CancellationToken ct)
        {
            Attempts++;
            if (_remaining-- > 0) throw new HttpRequestExceptionStub();
            return Task.FromResult(true);
        }
    }

    private sealed class HttpRequestExceptionStub : Exception { }
}
