using System.Security.Cryptography;
using System.Text;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

public class VaultServiceTests
{
    private static readonly KdfParams Light = new(MemoryKiB: 8192, Iterations: 2, Parallelism: 1);
    private const string Master = "correct horse battery staple";
    private static byte[] Content() => Encoding.UTF8.GetBytes("""{"entries":[]}""");

    [Fact]
    public void Create_then_OpenWithMaster_returns_original_content()
    {
        var content = Content();
        var result = VaultService.Create(Master, content, Light);

        var opened = VaultService.OpenWithMaster(result.Vault, Master);

        Assert.Equal(content, opened);
    }

    [Fact]
    public void Create_then_OpenWithRecoveryKey_returns_original_content()
    {
        var content = Content();
        var result = VaultService.Create(Master, content, Light);

        var opened = VaultService.OpenWithRecoveryKey(result.Vault, result.RecoveryKey);

        Assert.Equal(content, opened);
    }

    [Fact]
    public void Create_returns_recovery_key_of_expected_size()
    {
        var result = VaultService.Create(Master, Content(), Light);

        Assert.Equal(VaultService.RecoveryKeySizeBytes, result.RecoveryKey.Length);
    }

    [Fact]
    public void OpenWithMaster_with_wrong_password_throws_InvalidMasterPassword()
    {
        var result = VaultService.Create(Master, Content(), Light);

        Assert.Throws<InvalidMasterPasswordException>(
            () => VaultService.OpenWithMaster(result.Vault, "wrong-password"));
    }

    [Fact]
    public void OpenWithRecoveryKey_with_wrong_key_throws_InvalidRecoveryKey()
    {
        var result = VaultService.Create(Master, Content(), Light);
        var wrongKey = RandomNumberGenerator.GetBytes(VaultService.RecoveryKeySizeBytes);

        Assert.Throws<InvalidRecoveryKeyException>(
            () => VaultService.OpenWithRecoveryKey(result.Vault, wrongKey));
    }

    [Fact]
    public void OpenWithMaster_with_tampered_body_throws_VaultCorrupted()
    {
        var result = VaultService.Create(Master, Content(), Light);
        result.Vault.Ciphertext[0] ^= 0xFF; // 본문 변조 = 파일 손상

        Assert.Throws<VaultCorruptedException>(
            () => VaultService.OpenWithMaster(result.Vault, Master));
    }

    [Fact]
    public void ChangeMasterPassword_then_open_with_new_password_returns_content()
    {
        var content = Content();
        var result = VaultService.Create(Master, content, Light);

        var rekeyed = VaultService.ChangeMasterPassword(result.Vault, Master, "new-master-passphrase", Light);
        var opened = VaultService.OpenWithMaster(rekeyed, "new-master-passphrase");

        Assert.Equal(content, opened);
    }

    [Fact]
    public void ChangeMasterPassword_old_password_no_longer_works()
    {
        var result = VaultService.Create(Master, Content(), Light);

        var rekeyed = VaultService.ChangeMasterPassword(result.Vault, Master, "new-master-passphrase", Light);

        Assert.Throws<InvalidMasterPasswordException>(
            () => VaultService.OpenWithMaster(rekeyed, Master));
    }

    [Fact]
    public void ChangeMasterPassword_recovery_key_still_works()
    {
        // 마스터·복구 두 경로 모두로 정상 복호화 (design 12장).
        var content = Content();
        var result = VaultService.Create(Master, content, Light);

        var rekeyed = VaultService.ChangeMasterPassword(result.Vault, Master, "new-master-passphrase", Light);
        var opened = VaultService.OpenWithRecoveryKey(rekeyed, result.RecoveryKey);

        Assert.Equal(content, opened);
    }

    [Fact]
    public void ChangeMasterPassword_does_not_reencrypt_body()
    {
        // 2단 키의 핵심 이점: 본문은 그대로, DEK 래핑만 갱신(TD-006).
        var result = VaultService.Create(Master, Content(), Light);

        var rekeyed = VaultService.ChangeMasterPassword(result.Vault, Master, "new-master-passphrase", Light);

        Assert.Equal(result.Vault.Ciphertext, rekeyed.Ciphertext);
        Assert.Equal(result.Vault.Nonce, rekeyed.Nonce);
    }

    [Fact]
    public void ChangeMasterPassword_with_wrong_current_password_throws()
    {
        var result = VaultService.Create(Master, Content(), Light);

        Assert.Throws<InvalidMasterPasswordException>(
            () => VaultService.ChangeMasterPassword(result.Vault, "wrong-current", "new-master", Light));
    }

    [Fact]
    public void Unlock_returns_content_and_dek()
    {
        var content = Content();
        var result = VaultService.Create(Master, content, Light);

        var session = VaultService.Unlock(result.Vault, Master);

        Assert.Equal(content, session.Content);
        Assert.Equal(VaultCrypto.KeySizeBytes, session.Dek.Length);
    }

    [Fact]
    public void Unlock_with_wrong_password_throws_InvalidMasterPassword()
    {
        var result = VaultService.Create(Master, Content(), Light);

        Assert.Throws<InvalidMasterPasswordException>(
            () => VaultService.Unlock(result.Vault, "wrong-password"));
    }

    [Fact]
    public void SealBody_updates_content_readable_with_same_master()
    {
        var result = VaultService.Create(Master, Content(), Light);
        var session = VaultService.Unlock(result.Vault, Master);
        var newContent = Encoding.UTF8.GetBytes("""{"entries":["updated"]}""");

        var resealed = VaultService.SealBody(result.Vault, session.Dek, newContent);

        Assert.Equal(newContent, VaultService.OpenWithMaster(resealed, Master));
    }

    [Fact]
    public void SealBody_keeps_header_so_recovery_key_still_works()
    {
        var result = VaultService.Create(Master, Content(), Light);
        var session = VaultService.Unlock(result.Vault, Master);
        var newContent = Encoding.UTF8.GetBytes("""{"entries":["updated"]}""");

        var resealed = VaultService.SealBody(result.Vault, session.Dek, newContent);

        Assert.Equal(newContent, VaultService.OpenWithRecoveryKey(resealed, result.RecoveryKey));
    }

    [Fact]
    public void SealBody_uses_fresh_nonce_each_time()
    {
        var result = VaultService.Create(Master, Content(), Light);
        var session = VaultService.Unlock(result.Vault, Master);
        var body = Encoding.UTF8.GetBytes("same content");

        var a = VaultService.SealBody(result.Vault, session.Dek, body);
        var b = VaultService.SealBody(result.Vault, session.Dek, body);

        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }

    // --- KDF 자동 상향 (M5, design 7.5) ---

    private static readonly KdfParams Stronger = new(MemoryKiB: 16384, Iterations: 3, Parallelism: 2);

    [Fact]
    public void UpgradeKdf_stores_new_params_and_new_salt()
    {
        var result = VaultService.Create(Master, Content(), Light);
        var session = VaultService.Unlock(result.Vault, Master);

        var upgraded = VaultService.UpgradeKdf(result.Vault, Master, session.Dek, Stronger);

        Assert.Equal(Stronger, upgraded.Header.Kdf);
        Assert.NotEqual(result.Vault.Header.Salt, upgraded.Header.Salt);   // 새 salt
        Assert.NotEqual(result.Vault.Header.DekByMaster, upgraded.Header.DekByMaster); // 재래핑
    }

    [Fact]
    public void UpgradeKdf_opens_with_same_master_and_preserves_content()
    {
        var content = Content();
        var result = VaultService.Create(Master, content, Light);
        var session = VaultService.Unlock(result.Vault, Master);

        var upgraded = VaultService.UpgradeKdf(result.Vault, Master, session.Dek, Stronger);

        Assert.Equal(content, VaultService.OpenWithMaster(upgraded, Master)); // 같은 비번 유지
    }

    [Fact]
    public void UpgradeKdf_does_not_reencrypt_body_and_keeps_recovery()
    {
        var content = Content();
        var result = VaultService.Create(Master, content, Light);
        var session = VaultService.Unlock(result.Vault, Master);

        var upgraded = VaultService.UpgradeKdf(result.Vault, Master, session.Dek, Stronger);

        Assert.Equal(result.Vault.Ciphertext, upgraded.Ciphertext);        // 본문 그대로
        Assert.Equal(content, VaultService.OpenWithRecoveryKey(upgraded, result.RecoveryKey)); // 복구 경로 유지
    }
}
