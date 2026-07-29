using System.Security.Cryptography;
using System.Text;
using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class VaultCryptoTests
{
    private static byte[] RandomBytes(int n) => RandomNumberGenerator.GetBytes(n);

    private static byte[] Key() => RandomBytes(VaultCrypto.KeySizeBytes);
    private static byte[] Nonce() => RandomBytes(VaultCrypto.NonceSizeBytes);

    [Fact]
    public void Encrypt_then_Decrypt_returns_original_plaintext()
    {
        var key = Key();
        var nonce = Nonce();
        var plaintext = Encoding.UTF8.GetBytes("my-secret-password");

        var sealed_ = VaultCrypto.Encrypt(key, nonce, plaintext);
        var decrypted = VaultCrypto.Decrypt(key, nonce, sealed_);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Ciphertext_differs_from_plaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("my-secret-password");
        var sealed_ = VaultCrypto.Encrypt(Key(), Nonce(), plaintext);

        Assert.NotEqual(plaintext, sealed_.Ciphertext);
    }

    [Fact]
    public void Decrypt_with_wrong_key_throws()
    {
        var nonce = Nonce();
        var sealed_ = VaultCrypto.Encrypt(Key(), nonce, RandomBytes(32));

        Assert.Throws<AuthenticationTagMismatchException>(
            () => VaultCrypto.Decrypt(Key(), nonce, sealed_));
    }

    [Fact]
    public void Decrypt_with_tampered_ciphertext_throws()
    {
        var key = Key();
        var nonce = Nonce();
        var sealed_ = VaultCrypto.Encrypt(key, nonce, RandomBytes(32));
        sealed_.Ciphertext[0] ^= 0xFF; // 한 비트 변조

        Assert.Throws<AuthenticationTagMismatchException>(
            () => VaultCrypto.Decrypt(key, nonce, sealed_));
    }

    [Fact]
    public void Decrypt_with_tampered_tag_throws()
    {
        var key = Key();
        var nonce = Nonce();
        var sealed_ = VaultCrypto.Encrypt(key, nonce, RandomBytes(32));
        sealed_.Tag[0] ^= 0xFF; // 태그 변조

        Assert.Throws<AuthenticationTagMismatchException>(
            () => VaultCrypto.Decrypt(key, nonce, sealed_));
    }

    [Fact]
    public void Same_plaintext_with_different_nonce_produces_different_ciphertext()
    {
        var key = Key();
        var plaintext = RandomBytes(32);

        var a = VaultCrypto.Encrypt(key, Nonce(), plaintext);
        var b = VaultCrypto.Encrypt(key, Nonce(), plaintext);

        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }
}
