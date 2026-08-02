using System.Security.Cryptography;
using System.Text;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests;

/// <summary>복구 키로 잠근 내보내기 파일(TD-050)의 봉인·해제 검증.</summary>
public class EncryptedExportTests
{
    private const string Csv = "title,url,login,password,notes,tags\r\nSteam,,gamer,s3cr3t,,\r\n";

    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Protect_then_Unprotect_roundtrips()
    {
        var key = Key();

        var file = EncryptedExport.Protect(key, Csv);

        Assert.Equal(Csv, EncryptedExport.Unprotect(key, file));
    }

    [Fact]
    public void Protected_file_does_not_leak_plaintext()
    {
        var file = EncryptedExport.Protect(Key(), Csv);

        // 비밀번호·아이디가 파일 어디에도 평문으로 남아선 안 된다.
        var text = Encoding.UTF8.GetString(file);
        Assert.DoesNotContain("s3cr3t", text);
        Assert.DoesNotContain("gamer", text);
    }

    [Fact]
    public void Protect_uses_fresh_nonce_each_time()
    {
        var key = Key();

        var first = EncryptedExport.Protect(key, Csv);
        var second = EncryptedExport.Protect(key, Csv);

        Assert.NotEqual(first, second); // 같은 키·같은 내용이어도 암호문이 달라야 한다
    }

    [Fact]
    public void Unprotect_with_wrong_key_throws_InvalidRecoveryKey()
    {
        var file = EncryptedExport.Protect(Key(), Csv);

        Assert.Throws<InvalidRecoveryKeyException>(() => EncryptedExport.Unprotect(Key(), file));
    }

    [Fact]
    public void Unprotect_rejects_tampered_ciphertext()
    {
        var key = Key();
        var file = EncryptedExport.Protect(key, Csv);
        file[^1] ^= 0xFF; // 마지막 바이트 변조

        Assert.Throws<InvalidRecoveryKeyException>(() => EncryptedExport.Unprotect(key, file));
    }

    [Fact]
    public void Unprotect_rejects_tampered_header()
    {
        var key = Key();
        var file = EncryptedExport.Protect(key, Csv);
        file[0] ^= 0xFF; // 매직 훼손

        Assert.Throws<FormatException>(() => EncryptedExport.Unprotect(key, file));
    }

    [Fact]
    public void Unprotect_rejects_file_that_is_too_short()
    {
        Assert.Throws<FormatException>(() => EncryptedExport.Unprotect(Key(), new byte[4]));
    }

    [Fact]
    public void Unprotect_rejects_plain_csv_file()
    {
        // 평문 CSV를 실수로 고른 경우 — 암호가 아니라 형식 문제로 알려줘야 한다.
        var plain = Encoding.UTF8.GetBytes(Csv);

        Assert.Throws<FormatException>(() => EncryptedExport.Unprotect(Key(), plain));
    }
}
