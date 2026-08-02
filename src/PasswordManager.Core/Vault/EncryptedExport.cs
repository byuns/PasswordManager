using System.Security.Cryptography;
using System.Text;
using PasswordManager.Core.Security;

namespace PasswordManager.Core.Vault;

/// <summary>
/// 내보내기 CSV를 복구 키로 잠근 파일로 봉인하고 되돌린다(TD-050).
/// 복구 키는 256-bit 고엔트로피 랜덤이라(<see cref="VaultService.RecoveryKeySizeBytes"/>)
/// KDF 없이 그대로 AES-256-GCM 키로 쓴다 — 볼트 헤더가 복구 래핑에 쓰는 방식과 같다(TD-010).
/// <para>
/// 파일 구조: <c>magic(8) + nonce(12) + tag(16) + ciphertext</c>. magic은 AEAD의 연관 데이터로도
/// 넣어, 헤더만 바꿔치기해도 복호화가 실패한다.
/// </para>
/// <para>
/// 이 파일은 <b>봉인할 때 쓴 복구 키로만</b> 열린다. 복구 키를 재발급하면(TD-035) 볼트는 새 키를
/// 쓰지만 이미 내보낸 파일은 옛 키로 남는다 — 암호화의 당연한 성질이며 호출부가 사용자에게 알린다.
/// </para>
/// </summary>
public static class EncryptedExport
{
    /// <summary>파일 식별자 겸 포맷 버전. 구조가 바뀌면 끝자리를 올린다.</summary>
    private static readonly byte[] Magic = "PWMEXP01"u8.ToArray();

    /// <summary>헤더(매직+nonce+태그) 길이. 이보다 짧으면 이 포맷이 아니다.</summary>
    private static readonly int HeaderSize =
        Magic.Length + VaultCrypto.NonceSizeBytes + VaultCrypto.TagSizeBytes;

    /// <summary>CSV를 복구 키로 암호화한 파일 바이트로 만든다.</summary>
    public static byte[] Protect(byte[] recoveryKey, string csv)
    {
        var plaintext = Encoding.UTF8.GetBytes(csv);
        var nonce = RandomNumberGenerator.GetBytes(VaultCrypto.NonceSizeBytes);
        var sealed_ = VaultCrypto.Encrypt(recoveryKey, nonce, plaintext, Magic);
        MemoryHygiene.Clear(plaintext); // 평문 CSV 버퍼를 소거(design 5.5)

        var file = new byte[HeaderSize + sealed_.Ciphertext.Length];
        var at = 0;
        Magic.CopyTo(file, at); at += Magic.Length;
        nonce.CopyTo(file, at); at += nonce.Length;
        sealed_.Tag.CopyTo(file, at); at += sealed_.Tag.Length;
        sealed_.Ciphertext.CopyTo(file, at);
        return file;
    }

    /// <summary>
    /// 잠긴 파일을 복구 키로 풀어 CSV를 돌려준다. 이 포맷이 아니면 <see cref="FormatException"/>,
    /// 키가 틀리거나 파일이 변조됐으면 <see cref="InvalidRecoveryKeyException"/>을 던진다.
    /// </summary>
    public static string Unprotect(byte[] recoveryKey, byte[] file)
    {
        if (file.Length < HeaderSize || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new FormatException("잠긴 내보내기 파일이 아닙니다.");

        var at = Magic.Length;
        var nonce = file.AsSpan(at, VaultCrypto.NonceSizeBytes).ToArray();
        at += VaultCrypto.NonceSizeBytes;
        var tag = file.AsSpan(at, VaultCrypto.TagSizeBytes).ToArray();
        at += VaultCrypto.TagSizeBytes;
        var ciphertext = file.AsSpan(at).ToArray();

        byte[] plaintext;
        try
        {
            plaintext = VaultCrypto.Decrypt(recoveryKey, nonce, new AeadResult(ciphertext, tag), Magic);
        }
        catch (CryptographicException)
        {
            // 키가 틀렸든 파일이 변조됐든 AEAD는 구분하지 않는다 — 사용자에게는 같은 이야기다.
            throw new InvalidRecoveryKeyException();
        }

        var csv = Encoding.UTF8.GetString(plaintext);
        MemoryHygiene.Clear(plaintext);
        return csv;
    }
}
