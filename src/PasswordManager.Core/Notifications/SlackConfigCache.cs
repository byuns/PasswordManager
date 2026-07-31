namespace PasswordManager.Core.Notifications;

/// <summary>
/// 슬랙 게이트 설정의 세션 메모리 캐시(TD-012 로그인 실패 알림, A안). Webhook URL은 볼트 안에
/// 암호화 저장돼 잠긴 상태에선 읽을 수 없으므로, 한 번 잠금 해제할 때 설정 스냅샷을 여기 담아 둔다.
/// 이후 볼트가 잠겨도(자리 비움) 마지막 스냅샷으로 "로그인 실패" 알림을 보낼 수 있다. 콜드 스타트
/// (한 번도 열지 않은 상태)에선 <see cref="Current"/>가 null이라 실패 알림도 나가지 않는다.
/// </summary>
public sealed class SlackConfigCache
{
    private volatile SlackConfig? _current;

    /// <summary>마지막으로 알려진 설정 스냅샷. 아직 한 번도 열지 않았으면 null.</summary>
    public SlackConfig? Current => _current;

    /// <summary>잠금 해제·설정 저장 시 최신 스냅샷으로 갱신한다.</summary>
    public void Update(SlackConfig config) => _current = config;
}
