using System.Text.RegularExpressions;

namespace PasswordManager.Core.Notifications;

/// <summary>슬랙 알림이 나타내는 이벤트 종류. 각 종류는 한국어 라벨로 표시된다(design 7.8).</summary>
public enum SlackEvent
{
    /// <summary>잠금 해제 성공.</summary>
    Unlock,
    /// <summary>마스터 비밀번호 로그인 실패.</summary>
    LoginFailure,
    /// <summary>비밀번호 추가·변경.</summary>
    PasswordChange,
    /// <summary>(선택) 마스터 비밀번호 변경.</summary>
    MasterPasswordChanged,
    /// <summary>(선택) 복구 키 재발급.</summary>
    RecoveryKeyReissued,
}

/// <summary>
/// 슬랙 알림 문구를 템플릿에서 렌더한다(TD-014). 허용 변수(<c>{이벤트}</c>·<c>{시각}</c>·<c>{기기명}</c>)만
/// 치환하고, 화이트리스트 밖 토큰은 <b>리터럴 그대로</b> 남긴다. secret은 변수 자체를 제공하지 않으므로
/// 어떤 템플릿을 써도 비밀번호·아이디 등이 새어 나갈 수 없다(구조적 차단).
/// </summary>
public static class SlackMessageTemplate
{
    private static readonly Regex Token = new(@"\{([^{}]*)\}", RegexOptions.Compiled);
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>이벤트 종류의 한국어 라벨.</summary>
    public static string Label(SlackEvent kind) => kind switch
    {
        SlackEvent.Unlock => "잠금 해제됨",
        SlackEvent.LoginFailure => "로그인 실패",
        SlackEvent.PasswordChange => "비밀번호 변경",
        SlackEvent.MasterPasswordChanged => "마스터 비밀번호 변경",
        SlackEvent.RecoveryKeyReissued => "복구 키 재발급",
        _ => kind.ToString(),
    };

    /// <summary>
    /// 템플릿을 이벤트·시각·기기명으로 렌더한다. 시각은 넘어온 <see cref="DateTimeOffset"/>의 오프셋 기준으로
    /// <c>yyyy-MM-dd HH:mm</c> 포맷한다(호출부가 로컬 시각을 넘긴다). 기기명이 null이면 빈 문자열로 치환한다.
    /// </summary>
    public static string Render(string template, SlackEvent kind, DateTimeOffset time, string? deviceName)
    {
        var values = new Dictionary<string, string>
        {
            ["이벤트"] = Label(kind),
            ["시각"] = time.ToString(TimeFormat),
            ["기기명"] = deviceName ?? "",
        };

        // 화이트리스트에 있는 변수만 치환하고, 나머지 {…}는 원문 그대로 남긴다.
        return Token.Replace(template ?? "", m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }
}
