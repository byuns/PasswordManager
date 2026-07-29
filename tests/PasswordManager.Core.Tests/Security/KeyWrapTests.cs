using System.Security.Cryptography;
using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class KeyWrapTests
{
    private static byte[] RandomKey() => RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);

    [Fact]
    public void Wrap_then_Unwrap_returns_original_dek()
    {
        var kek = RandomKey();
        var dek = RandomKey();

        var wrapped = KeyWrap.Wrap(kek, dek);
        var unwrapped = KeyWrap.Unwrap(kek, wrapped);

        Assert.Equal(dek, unwrapped);
    }

    [Fact]
    public void Unwrap_with_wrong_key_throws()
    {
        var wrapped = KeyWrap.Wrap(RandomKey(), RandomKey());

        Assert.Throws<AuthenticationTagMismatchException>(
            () => KeyWrap.Unwrap(RandomKey(), wrapped));
    }

    [Fact]
    public void Wrapped_ciphertext_differs_from_dek()
    {
        var dek = RandomKey();
        var wrapped = KeyWrap.Wrap(RandomKey(), dek);

        Assert.NotEqual(dek, wrapped.Ciphertext);
    }

    [Fact]
    public void Wrap_uses_fresh_nonce_each_time()
    {
        var kek = RandomKey();
        var dek = RandomKey();

        var a = KeyWrap.Wrap(kek, dek);
        var b = KeyWrap.Wrap(kek, dek);

        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.Ciphertext, b.Ciphertext);
    }

    [Fact]
    public void Dek_double_wrapped_by_master_and_recovery_both_unwrap_to_same_dek()
    {
        // TD-006/010: 같은 DEK를 마스터키(KEK)·복구 키로 각각 감싸 저장한다.
        var dek = RandomKey();
        var masterKek = RandomKey();
        var recoveryKey = RandomKey();

        var byMaster = KeyWrap.Wrap(masterKek, dek);
        var byRecovery = KeyWrap.Wrap(recoveryKey, dek);

        Assert.Equal(dek, KeyWrap.Unwrap(masterKek, byMaster));
        Assert.Equal(dek, KeyWrap.Unwrap(recoveryKey, byRecovery));
    }

    [Fact]
    public void Unwrap_with_tampered_ciphertext_throws()
    {
        var kek = RandomKey();
        var wrapped = KeyWrap.Wrap(kek, RandomKey());
        wrapped.Ciphertext[0] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(
            () => KeyWrap.Unwrap(kek, wrapped));
    }
}
