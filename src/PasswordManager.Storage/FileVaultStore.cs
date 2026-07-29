using PasswordManager.Core.Vault;

namespace PasswordManager.Storage;

/// <summary>
/// 파일 시스템 기반 <see cref="IVaultFileStore"/> 구현. 정적 <see cref="VaultFileStore"/>에 위임한다.
/// Core의 <see cref="VaultManager"/>가 의존성 역전을 통해 이 어댑터를 주입받는다.
/// </summary>
public sealed class FileVaultStore : IVaultFileStore
{
    public bool Exists(string path) => File.Exists(path);

    public void Save(string path, EncryptedVault vault) => VaultFileStore.Save(path, vault);

    public EncryptedVault Load(string path) => VaultFileStore.Load(path);
}
