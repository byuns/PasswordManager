using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class TotpValidatorTests
{
    // RFC 6238 Appendix B의 공유 secret: ASCII "12345678901234567890"(20바이트)를 RFC4648 Base32로 인코딩.
    private const string Rfc6238Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void GenerateCode_matches_rfc6238_test_vectors(long unixSeconds, string expected)
    {
        var time = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        var code = TotpValidator.GenerateCode(Rfc6238Secret, time);

        Assert.Equal(expected, code);
    }

    [Fact]
    public void GenerateCode_is_six_digits_zero_padded()
    {
        var code = TotpValidator.GenerateCode(Rfc6238Secret, DateTimeOffset.FromUnixTimeSeconds(1234567890L));

        Assert.Equal(6, code.Length);
        Assert.All(code, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void GenerateSecret_produces_decodable_base32_of_requested_length()
    {
        var secret = TotpValidator.GenerateSecret();

        // 유효한 RFC4648 Base32 문자만 사용(A-Z2-7).
        const string base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        Assert.All(secret, c => Assert.Contains(c, base32));
        // 같은 secret으로 코드가 계산되면(예외 없음) 디코딩 가능한 것으로 간주.
        _ = TotpValidator.GenerateCode(secret, DateTimeOffset.FromUnixTimeSeconds(0));
    }

    [Fact]
    public void GenerateSecret_returns_distinct_values()
    {
        Assert.NotEqual(TotpValidator.GenerateSecret(), TotpValidator.GenerateSecret());
    }

    [Fact]
    public void Verify_accepts_current_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);
        var code = TotpValidator.GenerateCode(Rfc6238Secret, now);

        Assert.True(TotpValidator.Verify(Rfc6238Secret, code, now));
    }

    [Fact]
    public void Verify_allows_one_step_clock_skew()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);
        var prev = TotpValidator.GenerateCode(Rfc6238Secret, now.AddSeconds(-30));
        var next = TotpValidator.GenerateCode(Rfc6238Secret, now.AddSeconds(30));

        Assert.True(TotpValidator.Verify(Rfc6238Secret, prev, now));
        Assert.True(TotpValidator.Verify(Rfc6238Secret, next, now));
    }

    [Fact]
    public void Verify_rejects_code_beyond_skew_window()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);
        var twoStepsAgo = TotpValidator.GenerateCode(Rfc6238Secret, now.AddSeconds(-60));

        Assert.False(TotpValidator.Verify(Rfc6238Secret, twoStepsAgo, now));
    }

    [Fact]
    public void Verify_rejects_wrong_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);

        Assert.False(TotpValidator.Verify(Rfc6238Secret, "000000", now));
    }

    [Fact]
    public void Verify_ignores_spaces_in_user_entered_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000L);
        var code = TotpValidator.GenerateCode(Rfc6238Secret, now);
        var spaced = code.Insert(3, " ");

        Assert.True(TotpValidator.Verify(Rfc6238Secret, spaced, now));
    }
}
