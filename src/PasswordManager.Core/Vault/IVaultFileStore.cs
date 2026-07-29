namespace PasswordManager.Core.Vault;

/// <summary>
/// 볼트 파일 영속화 추상화. Core는 파일 I/O 구현(Storage)에 의존하지 않고 이 인터페이스에만 의존한다.
/// 구현체는 PasswordManager.Storage 계층에서 제공한다(의존성 역전).
/// </summary>
public interface IVaultFileStore
{
    /// <summary>해당 경로에 볼트 파일이 존재하는가.</summary>
    bool Exists(string path);

    /// <summary>암호화된 볼트를 원자적으로 저장한다.</summary>
    void Save(string path, EncryptedVault vault);

    /// <summary>암호화된 볼트를 읽어 온다.</summary>
    EncryptedVault Load(string path);
}
