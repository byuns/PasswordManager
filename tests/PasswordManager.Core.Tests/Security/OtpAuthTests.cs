using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class OtpAuthTests
{
    private const string Secret = "GEZDGNBVGY3TQOJQ";

    [Fact]
    public void BuildUri_starts_with_totp_scheme_and_label()
    {
        var uri = OtpAuth.BuildUri(Secret, "PasswordManager", "Vault Unlock");

        // 표준 형식: otpauth://totp/{issuer}:{account}?...  (라벨은 URL 인코딩)
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("PasswordManager%3AVault%20Unlock", uri);
    }

    [Fact]
    public void BuildUri_carries_secret_and_standard_params()
    {
        var uri = OtpAuth.BuildUri(Secret, "PasswordManager", "Vault Unlock");

        Assert.Contains($"secret={Secret}", uri);
        Assert.Contains("issuer=PasswordManager", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }
}
