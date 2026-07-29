using System.Text.Json.Nodes;

namespace PasswordManager.Core.Models;

/// <summary>볼트 파일이 현재 앱보다 새 버전에서 만들어져 열 수 없는 경우(TD-008).</summary>
public sealed class VaultVersionException(int fileVersion, int supportedVersion)
    : Exception($"이 볼트는 더 새 버전(v{fileVersion})의 앱에서 만들어졌습니다. 현재 앱은 v{supportedVersion}까지 지원합니다.")
{
    public int FileVersion { get; } = fileVersion;
    public int SupportedVersion { get; } = supportedVersion;
}

/// <summary>
/// 볼트 본문 JSON을 현재 스키마 버전으로 끌어올린다(TD-008). 구조 변경은 버전별 변환 단계로,
/// 단순 필드 추가는 역직렬화 기본값으로 흡수한다. 버전이 없으면 v1(legacy)로 간주하고,
/// 현재보다 새 버전이면 <see cref="VaultVersionException"/>으로 거부한다.
/// </summary>
public static class VaultMigrator
{
    // key = from 버전, 값 = 그 버전을 (from+1)로 올리는 변환. 현재 최신이 v1이라 등록된 단계 없음.
    // v2 도입 시: Steps[1] = root => { ...구조 변경...; return root; }
    private static readonly Dictionary<int, Func<JsonObject, JsonObject>> Steps = new();

    /// <summary>root JSON을 <see cref="VaultData.CurrentVersion"/>까지 순차 변환하고 version을 정규화한다.</summary>
    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["version"]?.GetValue<int>() ?? 1; // 버전 필드 없으면 legacy v1

        if (version > VaultData.CurrentVersion)
            throw new VaultVersionException(version, VaultData.CurrentVersion);

        while (version < VaultData.CurrentVersion)
        {
            root = Steps[version](root);
            version++;
        }

        root["version"] = VaultData.CurrentVersion;
        return root;
    }
}
