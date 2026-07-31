using System.Net.Http;
using System.Text;
using PasswordManager.Core.Notifications;

namespace PasswordManager.App.Services;

/// <summary>
/// <see cref="IWebhookClient"/>의 HTTPS 구현(슬랙 Incoming Webhook, design 7.8). 짧은 타임아웃으로
/// POST하고, 2xx면 성공으로 본다. 예외·비2xx는 false로 돌려 조율기(<see cref="SlackNotifier"/>)가
/// 재시도·베스트에포트 처리를 하게 한다. 앱 동작을 막지 않도록 여기서 예외를 던지지 않는다.
/// </summary>
public sealed class HttpWebhookClient : IWebhookClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<bool> PostAsync(string url, string jsonPayload, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false; // 네트워크 실패·타임아웃 등은 실패로 흡수
        }
    }
}
