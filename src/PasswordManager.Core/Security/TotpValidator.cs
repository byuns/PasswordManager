using System.Security.Cryptography;

namespace PasswordManager.Core.Security;

/// <summary>
/// TOTP(RFC 6238) secret 생성·코드 계산·검증. 앱 잠금해제 2FA의 "열람 재확인 게이트"에 쓰인다
/// (design 5.4, TD-004). HMAC-SHA1·30초 주기·6자리 표준을 .NET 내장 암호로 직접 구현하며
/// (의존성 0, TD-001 기조), 검증 시 앞뒤 1스텝(±30초) 시계 오차를 허용한다.
/// secret은 Google Authenticator 호환을 위해 RFC 4648 Base32(A-Z2-7)로 인코딩한다.
/// </summary>
public static class TotpValidator
{
    private const int PeriodSeconds = 30;
    private const int Digits = 6;
    private const int DefaultSecretBytes = 20; // 160비트 — SHA1 권장 크기

    /// <summary>새 무작위 secret을 만들어 RFC4648 Base32 문자열로 반환한다.</summary>
    public static string GenerateSecret(int byteLength = DefaultSecretBytes)
    {
        var raw = RandomNumberGenerator.GetBytes(byteLength);
        return Base32Encode(raw);
    }

    /// <summary>주어진 시각의 TOTP 코드(6자리, 0 패딩)를 계산한다.</summary>
    public static string GenerateCode(string base32Secret, DateTimeOffset time)
    {
        var counter = time.ToUnixTimeSeconds() / PeriodSeconds;
        return Compute(Base32Decode(base32Secret), counter);
    }

    /// <summary>
    /// 사용자가 입력한 코드를 검증한다. 공백을 무시하고, 현재 스텝 기준 앞뒤
    /// <paramref name="skewSteps"/>스텝(기본 ±1, 즉 ±30초)까지 허용한다.
    /// </summary>
    public static bool Verify(string base32Secret, string code, DateTimeOffset time, int skewSteps = 1)
    {
        var normalized = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length != Digits)
            return false;

        var key = Base32Decode(base32Secret);
        var current = time.ToUnixTimeSeconds() / PeriodSeconds;
        for (var offset = -skewSteps; offset <= skewSteps; offset++)
        {
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(Compute(key, current + offset)),
                    System.Text.Encoding.ASCII.GetBytes(normalized)))
                return true;
        }
        return false;
    }

    /// <summary>RFC 4226 동적 절단으로 counter에 대한 6자리 코드를 만든다.</summary>
    private static string Compute(byte[] key, long counter)
    {
        var counterBytes = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString().PadLeft(Digits, '0');
    }

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var result = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(Base32Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0)
            result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return result.ToString();
    }

    private static byte[] Base32Decode(string encoded)
    {
        var bytes = new List<byte>(encoded.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var raw in encoded)
        {
            if (raw is '=' or '-' or ' ')
                continue;

            var value = Base32Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (value < 0)
                throw new FormatException($"올바르지 않은 Base32 문자입니다: '{raw}'");

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}
