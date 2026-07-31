namespace PasswordManager.Core.Notifications;

/// <summary>
/// <see cref="ISlackFailureLog"/>의 파일 구현. 슬랙 전송이 최종 실패(재시도까지 실패)했을 때
/// <c>{시각(ISO 8601)}\t{이벤트종류}</c> 한 줄만 append 한다. secret(Webhook URL·사이트명)은
/// 애초에 인자로 받지 않으므로 파일에 남지 않는다(design 7.8). 알림이 베스트에포트이므로
/// 로그 기록 자체가 실패해도 예외를 던지지 않고 삼킨다.
/// 파일이 상한(<see cref="_maxBytes"/>)을 넘으면 최근 절반만 남기고 잘라 무한 성장을 막는다
/// (비밀번호 이력의 상한 관리와 같은 철학).
/// </summary>
public sealed class FileSlackFailureLog : ISlackFailureLog
{
    private const long DefaultMaxBytes = 256 * 1024; // 256KB. 한 줄 ~46B 기준 약 5천 건.

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new(); // fire-and-forget 동시 호출 대비 직렬화

    public FileSlackFailureLog(string path, long maxBytes = DefaultMaxBytes)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public void RecordFailure(SlackEvent kind, DateTimeOffset time)
    {
        var line = $"{time:o}\t{kind}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(_path, line);
                TrimIfOverCap();
            }
            catch
            {
                // 로깅 실패는 무시한다 — 관측용 보조 기능이 앱 흐름을 막으면 안 된다.
            }
        }
    }

    /// <summary>파일이 상한을 넘으면 오래된 앞쪽 절반을 버리고 최근 절반만 다시 쓴다.</summary>
    private void TrimIfOverCap()
    {
        if (new FileInfo(_path).Length <= _maxBytes) return;

        var lines = File.ReadAllLines(_path);
        var recent = lines.Skip(lines.Length / 2).ToArray();
        File.WriteAllLines(_path, recent);
    }
}
