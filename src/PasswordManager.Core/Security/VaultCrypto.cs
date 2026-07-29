using System.Security.Cryptography;

namespace PasswordManager.Core.Security;

/// <summary>
/// AES-256-GCM 인증 암호화 (design 5.2). 기밀성 + 무결성(인증 태그)을 동시에 제공한다.
/// 변조된 데이터나 잘못된 키로는 복호화 자체가 실패한다.
/// </summary>
public static class VaultCrypto
{
    /// <summary>대칭 키 길이 (256-bit).</summary>
    public const int KeySizeBytes = 32;

    /// <summary>GCM nonce 길이 (96-bit 권장).</summary>
    public const int NonceSizeBytes = 12;

    /// <summary>GCM 인증 태그 길이 (128-bit).</summary>
    public const int TagSizeBytes = 16;

    /// <summary>평문을 암호화해 암호문과 인증 태그를 반환한다.</summary>
    public static AeadResult Encrypt(byte[] key, byte[] nonce, byte[] plaintext, byte[]? associatedData = null)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new AeadResult(ciphertext, tag);
    }

    /// <summary>암호문을 복호화해 평문을 반환한다. 인증 실패 시 예외를 던진다.</summary>
    public static byte[] Decrypt(byte[] key, byte[] nonce, AeadResult data, byte[]? associatedData = null)
    {
        var plaintext = new byte[data.Ciphertext.Length];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, data.Ciphertext, data.Tag, plaintext, associatedData);
        return plaintext;
    }
}

/// <summary>AEAD 암호화 결과: 암호문 + 인증 태그.</summary>
public sealed record AeadResult(byte[] Ciphertext, byte[] Tag);
