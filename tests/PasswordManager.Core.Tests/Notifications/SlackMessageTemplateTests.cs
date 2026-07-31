using PasswordManager.Core.Notifications;

namespace PasswordManager.Core.Tests.Notifications;

public class SlackMessageTemplateTests
{
    // 고정 시각(오프셋 포함) — 로컬 변환 없이 자기 오프셋 기준으로 포맷되므로 결정적이다.
    private static readonly DateTimeOffset At = new(2026, 7, 31, 14, 3, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Renders_whitelisted_variables()
    {
        var text = SlackMessageTemplate.Render("{이벤트} · {시각} · {기기명}",
            SlackEvent.Unlock, At, deviceName: "데스크탑");

        Assert.Equal("잠금 해제됨 · 2026-07-31 14:03 · 데스크탑", text);
    }

    [Fact]
    public void Default_template_renders_expected_message()
    {
        var text = SlackMessageTemplate.Render(SlackSettingsDefaultTemplate,
            SlackEvent.Unlock, At, deviceName: null);

        Assert.Equal("🔓 잠금 해제됨 · 2026-07-31 14:03", text);
    }

    [Fact]
    public void Unknown_tokens_are_left_literal_to_block_secret_leaks()
    {
        // 화이트리스트 밖 토큰({비밀번호} 등)은 치환되지 않고 그대로 남는다 → secret 변수 자체가 없음(TD-014).
        var text = SlackMessageTemplate.Render("{이벤트} {비밀번호} {아이디}",
            SlackEvent.LoginFailure, At, null);

        Assert.Equal("로그인 실패 {비밀번호} {아이디}", text);
    }

    [Fact]
    public void Missing_device_name_becomes_empty()
    {
        var text = SlackMessageTemplate.Render("[{기기명}]", SlackEvent.Unlock, At, deviceName: null);

        Assert.Equal("[]", text);
    }

    [Fact]
    public void Repeated_variables_all_replaced()
    {
        var text = SlackMessageTemplate.Render("{이벤트}/{이벤트}", SlackEvent.PasswordChange, At, null);

        Assert.Equal("비밀번호 변경/비밀번호 변경", text);
    }

    [Fact]
    public void Text_without_braces_is_unchanged()
    {
        var text = SlackMessageTemplate.Render("고정 문구", SlackEvent.Unlock, At, null);

        Assert.Equal("고정 문구", text);
    }

    [Theory]
    [InlineData(SlackEvent.Unlock, "잠금 해제됨")]
    [InlineData(SlackEvent.LoginFailure, "로그인 실패")]
    [InlineData(SlackEvent.PasswordChange, "비밀번호 변경")]
    [InlineData(SlackEvent.MasterPasswordChanged, "마스터 비밀번호 변경")]
    [InlineData(SlackEvent.RecoveryKeyReissued, "복구 키 재발급")]
    public void Event_labels_are_korean(SlackEvent kind, string expected)
    {
        Assert.Equal(expected, SlackMessageTemplate.Label(kind));
    }

    // 편의: 기본 템플릿 상수를 모델에서 끌어와 테스트 가독성 유지.
    private const string SlackSettingsDefaultTemplate = "🔓 {이벤트} · {시각}";
}
