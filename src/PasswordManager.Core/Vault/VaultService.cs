using System.Security.Cryptography;
using PasswordManager.Core.Security;

namespace PasswordManager.Core.Vault;

/// <summary>볼트 헤더 — 복호화에 필요한 키 재료(평문 저장 대상). design 5.3.</summary>
public sealed record VaultHeader(byte[] Salt, KdfParams Kdf, WrappedKey DekByMaster, WrappedKey DekByRecovery);

/// <summary>암호화된 볼트 — 헤더 + 본문(AES-256-GCM 암호문). design 5.3.</summary>
public sealed record EncryptedVault(VaultHeader Header, byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>볼트 생성 결과 — 암호화 볼트 + 최초 1회 사용자에게 보여줄 복구 키(TD-010).</summary>
public sealed record VaultCreationResult(EncryptedVault Vault, byte[] RecoveryKey);

/// <summary>언락 결과 — 복호화된 본문 + 세션 동안 보유할 DEK(TD-017).</summary>
public sealed record UnlockedVault(byte[] Content, byte[] Dek);

/// <summary>마스터 비밀번호가 틀려 DEK 언래핑에 실패한 경우(design 5.3).</summary>
public sealed class InvalidMasterPasswordException() : Exception("마스터 비밀번호가 올바르지 않습니다.");

/// <summary>복구 키가 틀려 DEK 언래핑에 실패한 경우.</summary>
public sealed class InvalidRecoveryKeyException() : Exception("복구 키가 올바르지 않습니다.");

/// <summary>DEK는 풀렸으나 본문 인증 태그 검증에 실패한 경우 = 파일 손상(design 5.3).</summary>
public sealed class VaultCorruptedException() : Exception("볼트 파일이 손상되었습니다.");

/// <summary>
/// 볼트 생성/열기/마스터 비번 변경을 조합하는 오케스트레이션 (design 5.2, TD-006/010).
/// KDF·2단 키 래핑·AEAD를 엮되, 파일 I/O는 다루지 않는다(Storage 계층 담당).
/// </summary>
public static class VaultService
{
    /// <summary>복구 키 길이 (256-bit 고엔트로피 랜덤).</summary>
    public const int RecoveryKeySizeBytes = 32;

    /// <summary>새 볼트를 만든다. DEK를 마스터키·복구 키로 이중 래핑하고 본문을 DEK로 암호화한다.</summary>
    public static VaultCreationResult Create(string masterPassword, byte[] content, KdfParams kdf)
    {
        var dek = RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);
        var recoveryKey = RandomNumberGenerator.GetBytes(RecoveryKeySizeBytes);
        var salt = KeyDerivation.NewSalt();
        var kek = KeyDerivation.DeriveKey(masterPassword, salt, kdf);

        var dekByMaster = KeyWrap.Wrap(kek, dek);
        var dekByRecovery = KeyWrap.Wrap(recoveryKey, dek);

        var nonce = RandomNumberGenerator.GetBytes(VaultCrypto.NonceSizeBytes);
        var body = VaultCrypto.Encrypt(dek, nonce, content);

        var header = new VaultHeader(salt, kdf, dekByMaster, dekByRecovery);
        var vault = new EncryptedVault(header, nonce, body.Ciphertext, body.Tag);
        return new VaultCreationResult(vault, recoveryKey);
    }

    /// <summary>마스터 비밀번호로 볼트를 연다. 비번 오류/파일 손상을 구분해 예외를 던진다.</summary>
    public static byte[] OpenWithMaster(EncryptedVault vault, string masterPassword)
        => Unlock(vault, masterPassword).Content;

    /// <summary>
    /// 마스터 비밀번호로 볼트를 열되 DEK도 함께 반환한다(TD-017). 세션 동안 DEK를 보유하면
    /// 저장 때마다 KDF를 다시 돌리지 않고 <see cref="SealBody"/>로 본문만 재암호화할 수 있다.
    /// </summary>
    public static UnlockedVault Unlock(EncryptedVault vault, string masterPassword)
    {
        var dek = UnwrapDekWithMaster(vault.Header, masterPassword);
        return new UnlockedVault(DecryptBody(vault, dek), dek);
    }

    /// <summary>
    /// 세션 DEK로 본문만 새 내용으로 재암호화한다(TD-017). 헤더(salt·KDF·마스터/복구 래핑)는
    /// 그대로 유지하므로 마스터·복구 두 경로 모두 계속 유효하다. nonce는 매번 새로 생성한다.
    /// </summary>
    public static EncryptedVault SealBody(EncryptedVault vault, byte[] dek, byte[] newContent)
    {
        var nonce = RandomNumberGenerator.GetBytes(VaultCrypto.NonceSizeBytes);
        var body = VaultCrypto.Encrypt(dek, nonce, newContent);
        return vault with { Nonce = nonce, Ciphertext = body.Ciphertext, Tag = body.Tag };
    }

    /// <summary>복구 키로 볼트를 연다(마스터 비번 분실 시).</summary>
    public static byte[] OpenWithRecoveryKey(EncryptedVault vault, byte[] recoveryKey)
    {
        byte[] dek;
        try
        {
            dek = KeyWrap.Unwrap(recoveryKey, vault.Header.DekByRecovery);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidRecoveryKeyException();
        }
        return DecryptBody(vault, dek);
    }

    /// <summary>마스터 비밀번호를 바꾼다. DEK를 새 마스터키로 다시 감싸기만 하며 본문은 재암호화하지 않는다.</summary>
    public static EncryptedVault ChangeMasterPassword(
        EncryptedVault vault, string currentMasterPassword, string newMasterPassword, KdfParams newKdf)
    {
        var dek = UnwrapDekWithMaster(vault.Header, currentMasterPassword); // 현재 비번 확인 겸 DEK 확보

        var newSalt = KeyDerivation.NewSalt();
        var newKek = KeyDerivation.DeriveKey(newMasterPassword, newSalt, newKdf);
        var newDekByMaster = KeyWrap.Wrap(newKek, dek);

        var newHeader = vault.Header with { Salt = newSalt, Kdf = newKdf, DekByMaster = newDekByMaster };
        return vault with { Header = newHeader }; // 본문·복구 래핑은 그대로 유지
    }

    private static byte[] UnwrapDekWithMaster(VaultHeader header, string masterPassword)
    {
        var kek = KeyDerivation.DeriveKey(masterPassword, header.Salt, header.Kdf);
        try
        {
            return KeyWrap.Unwrap(kek, header.DekByMaster);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidMasterPasswordException();
        }
    }

    private static byte[] DecryptBody(EncryptedVault vault, byte[] dek)
    {
        try
        {
            return VaultCrypto.Decrypt(dek, vault.Nonce, new AeadResult(vault.Ciphertext, vault.Tag));
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new VaultCorruptedException();
        }
    }
}
