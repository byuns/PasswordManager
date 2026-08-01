using System.IO;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.App.Services;

/// <summary>
/// 파일 시스템 기반 <see cref="IFileEraser"/> 구현. 전체 초기화(TD-044)에서만 쓴다.
/// 삭제 대상 목록은 Core의 <see cref="Core.Vault.VaultReset"/>가 정하고, 여기서는 실제 삭제만 한다.
/// </summary>
public sealed class FileEraser : IFileEraser
{
    public bool Exists(string path) => File.Exists(path);

    public void Delete(string path) => File.Delete(path);
}
