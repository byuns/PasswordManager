using System.Text.Json;
using PasswordManager.Core.Vault;

namespace PasswordManager.Storage;

/// <summary>
/// 재시도 제한 상태를 볼트 옆 사이드카 파일(JSON)에 저장하는 <see cref="ILockoutStore"/> 구현.
/// 암호화 본문 밖이라 볼트가 잠긴 상태에서도 읽을 수 있어 앱 재시작 우회를 막는다. 파일이 없거나
/// 손상됐으면 초기 상태로 간주한다.
/// </summary>
public sealed class FileLockoutStore : ILockoutStore
{
    private readonly string _path;

    public FileLockoutStore(string path) => _path = path;

    /// <summary>볼트 경로에서 사이드카 경로(<c>&lt;vault&gt;.lockout</c>)를 만든다.</summary>
    public static FileLockoutStore ForVault(string vaultPath) => new(vaultPath + ".lockout");

    public LockoutState Load()
    {
        try
        {
            if (!File.Exists(_path)) return LockoutState.Empty;
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(_path));
            if (dto is null) return LockoutState.Empty;
            var until = string.IsNullOrEmpty(dto.LockedUntil)
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(dto.LockedUntil);
            return new LockoutState(dto.FailedAttempts, until);
        }
        catch
        {
            return LockoutState.Empty; // 손상 시 안전하게 초기화
        }
    }

    public void Save(LockoutState state)
    {
        var dto = new Dto(state.FailedAttempts, state.LockedUntil?.ToString("o"));
        File.WriteAllText(_path, JsonSerializer.Serialize(dto));
    }

    private sealed record Dto(int FailedAttempts, string? LockedUntil);
}
