namespace PasswordManager.Core.Security;

/// <summary>
/// 폰 Authenticator 앱이 인식하는 `otpauth://totp/...` 등록 URI를 만든다(Key URI Format).
/// TOTP 표준값(HMAC-SHA1·6자리·30초, TD-019)을 파라미터로 명시한다. QR 이미지는 UI 폴리싱
/// 단계에서 이 URI로 생성하고, 그 전에는 URI/secret 텍스트를 그대로 노출한다(TD-020).
/// </summary>
public static class OtpAuth
{
    public static string BuildUri(string base32Secret, string issuer, string account)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        var query =
            $"secret={base32Secret}" +
            $"&issuer={Uri.EscapeDataString(issuer)}" +
            "&algorithm=SHA1&digits=6&period=30";
        return $"otpauth://totp/{label}?{query}";
    }
}
