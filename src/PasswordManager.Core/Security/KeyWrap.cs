using System.Security.Cryptography;

namespace PasswordManager.Core.Security;

/// <summary>
/// 데이터키(DEK)를 다른 열쇠(KEK 또는 복구 키)로 감싸고(wrap) 다시 푸는(unwrap) 2단 키 구조 (design 5.2, TD-006/010).
/// 같은 DEK를 여러 열쇠로 각각 감싸 두면, 각 열쇠 중 하나만 있어도 DEK를 복원할 수 있다(마스터/복구 이중 래핑).
/// </summary>
public static class KeyWrap
{
    /// <summary>DEK를 wrappingKey로 감싼다. 저장할 때마다 새 nonce를 사용한다.</summary>
    public static WrappedKey Wrap(byte[] wrappingKey, byte[] dek)
    {
        var nonce = RandomNumberGenerator.GetBytes(VaultCrypto.NonceSizeBytes);
        var sealed_ = VaultCrypto.Encrypt(wrappingKey, nonce, dek);
        return new WrappedKey(nonce, sealed_.Ciphertext, sealed_.Tag);
    }

    /// <summary>감싼 DEK를 wrappingKey로 푼다. 잘못된 열쇠면 예외를 던진다.</summary>
    public static byte[] Unwrap(byte[] wrappingKey, WrappedKey wrapped)
        => VaultCrypto.Decrypt(wrappingKey, wrapped.Nonce, new AeadResult(wrapped.Ciphertext, wrapped.Tag));
}

/// <summary>감싼 데이터키: 복원에 필요한 nonce + 암호문 + 인증 태그. 볼트 헤더에 저장된다(design 5.3).</summary>
public sealed record WrappedKey(byte[] Nonce, byte[] Ciphertext, byte[] Tag);
