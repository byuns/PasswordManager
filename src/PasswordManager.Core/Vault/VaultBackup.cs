namespace PasswordManager.Core.Vault;

/// <summary>
/// 볼트 백업/복원 (M6). 볼트는 항상 완전 암호문이므로 언락 없이 파일을 그대로 복사한다. 복원은 대상이
/// 유효한 볼트 파일인지 <see cref="IVaultFileStore.Load"/>로 검증한 뒤 현재 경로를 교체한다. 복원 후에는
/// 백업 시점의 마스터 비밀번호로 다시 열어야 한다(헤더가 통째로 바뀌므로).
/// </summary>
public static class VaultBackup
{
    /// <summary>현재 볼트를 백업 경로로 복사한다(암호화 상태 그대로, 언락 불필요).</summary>
    public static void Backup(IVaultFileStore store, string vaultPath, string backupPath)
    {
        if (!store.Exists(vaultPath))
            throw new InvalidOperationException("백업할 볼트 파일이 없습니다.");
        store.Save(backupPath, store.Load(vaultPath));
    }

    /// <summary>백업 파일로 현재 볼트를 복원한다. 유효한 볼트인지 검증 후 교체한다.</summary>
    public static void Restore(IVaultFileStore store, string backupPath, string vaultPath)
    {
        if (!store.Exists(backupPath))
            throw new InvalidOperationException("복원할 백업 파일이 없습니다.");
        var vault = store.Load(backupPath); // 형식이 잘못됐으면 여기서 예외
        store.Save(vaultPath, vault);
    }
}
