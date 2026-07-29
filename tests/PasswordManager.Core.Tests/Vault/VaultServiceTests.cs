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
}
