using System.Text.Encodings.Web;
using System.Text.Json;
using PasswordManager.Core.Models;

namespace PasswordManager.Core.Notifications;

/// <summary>슬랙 알림을 이벤트 발생 시 보낸다(베스트에포트). design 7.8·TD-012.</summary>
public interface ISlackNotifier
{
    /// <summary>이벤트를 슬랙에 알린다. 게이트(전역·활성·토글)를 통과할 때만 전송하며, 실패해도 예외를 던지지 않는다.</summary>
    Task NotifyAsync(SlackEvent kind, DateTimeOffset time, string? siteName = null, CancellationToken ct = default);
}

/// <summary>실제 HTTPS POST를 수행하는 얇은 포트. App 계층이 <see cref="System.Net.Http.HttpClient"/>로 구현한다.</summary>
public interface IWebhookClient
{
    /// <summary>payload(JSON)를 url로 POST한다. 전송에 성공하면 true. 실패는 false 또는 예외로 알린다.</summary>
    Task<bool> PostAsync(string url, string jsonPayload, CancellationToken ct);
}

/// <summary>전송 실패를 기록하는 선택적 로그. secret은 넘기지 않으며 이벤트 종류·시각만 받는다(design 7.8).</summary>
public interface ISlackFailureLog
{
    void RecordFailure(SlackEvent kind, DateTimeOffset time);
}

/// <summary>슬랙 게이트 판단에 쓰는 설정 스냅샷. 잠긴 볼트에서는 provider가 null을 돌려준다.</summary>
public sealed record SlackConfig(bool NetworkAllowed, SlackSettings Settings);

/// <summary>
/// 슬랙 알림 조율기(A안). 매 호출마다 설정 스냅샷을 읽어 게이트(전역 오프라인·옵트인·이벤트 토글·URL)를
/// 판단하고, 통과 시 <see cref="SlackMessageTemplate"/>로 문구를 만들어 <see cref="IWebhookClient"/>로 보낸다.
/// 비동기·베스트에포트: 네트워크 실패·예외가 있어도 밖으로 던지지 않고 재시도 1회 후 조용히 실패 로그만 남긴다.
/// </summary>
public sealed class SlackNotifier : ISlackNotifier
{
    // 페이로드 text는 화이트리스트 템플릿 결과뿐이라 secret이 없다 → UTF-8 그대로 두어 가독성 확보.
    private static readonly JsonSerializerOptions PayloadOptions =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<SlackConfig?> _configProvider;
    private readonly IWebhookClient _client;
    private readonly ISlackFailureLog? _log;

    public SlackNotifier(Func<SlackConfig?> configProvider, IWebhookClient client, ISlackFailureLog? log = null)
    {
        _configProvider = configProvider;
        _client = client;
        _log = log;
    }

    public async Task NotifyAsync(SlackEvent kind, DateTimeOffset time, string? siteName = null,
        CancellationToken ct = default)
    {
        var cfg = _configProvider();
        if (cfg is null || !cfg.NetworkAllowed) return; // 잠김 또는 전역 차단(TD-013)

        var s = cfg.Settings;
        if (!s.Enabled || string.IsNullOrWhiteSpace(s.WebhookUrl)) return; // 옵트인 OFF·URL 없음
        if (!EventEnabled(s, kind)) return;                                 // 이벤트 토글 OFF

        var text = SlackMessageTemplate.Render(s.MessageTemplate, kind, time, s.DeviceName);
        if (s.IncludeSiteName && !string.IsNullOrWhiteSpace(siteName))
            text += $" · {siteName}";

        var payload = JsonSerializer.Serialize(new { text }, PayloadOptions);
        if (await TrySend(s.WebhookUrl, payload, ct)) return;
        if (await TrySend(s.WebhookUrl, payload, ct)) return; // 가벼운 재시도 1회

        _log?.RecordFailure(kind, time); // secret 배제: 종류·시각만
    }

    private async Task<bool> TrySend(string url, string payload, CancellationToken ct)
    {
        try
        {
            return await _client.PostAsync(url, payload, ct);
        }
        catch
        {
            return false; // 예외도 실패로 흡수(앱 동작을 막지 않음)
        }
    }

    private static bool EventEnabled(SlackSettings s, SlackEvent kind) => kind switch
    {
        SlackEvent.Unlock => s.NotifyUnlock,
        SlackEvent.LoginFailure => s.NotifyLoginFailure,
        SlackEvent.PasswordChange => s.NotifyPasswordChange,
        SlackEvent.MasterPasswordChanged or SlackEvent.RecoveryKeyReissued => s.NotifySensitive,
        _ => false,
    };
}
