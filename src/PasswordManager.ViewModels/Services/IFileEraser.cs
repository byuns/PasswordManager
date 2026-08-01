namespace PasswordManager.ViewModels.Services;

/// <summary>
/// 파일 삭제 추상화 (TD-044). 전체 초기화에서만 쓴다. <see cref="Core.Vault.IVaultFileStore"/>에
/// 삭제를 얹지 않고 따로 둔 이유는, 그 인터페이스의 구현체가 테스트 스텁을 포함해 16곳이라
/// 초기화 하나 때문에 전부 손대야 하기 때문이다(인터페이스 분리).
/// </summary>
public interface IFileEraser
{
    /// <summary>파일이 존재하는가.</summary>
    bool Exists(string path);

    /// <summary>파일을 지운다. 없으면 아무것도 하지 않는다.</summary>
    void Delete(string path);
}
