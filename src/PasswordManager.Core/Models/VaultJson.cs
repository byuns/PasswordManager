using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PasswordManager.Core.Models;

/// <summary>볼트 본문 JSON 직렬화. camelCase·ISO 8601. VaultFileStore가 본문 암호화 전/후 사용.</summary>
public static class VaultJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(VaultData data) => JsonSerializer.Serialize(data, Options);

    public static VaultData Deserialize(string json)
    {
        // 버전 확인·마이그레이션 후 역직렬화한다(TD-008). 없는 필드는 기본값으로 흡수(관대한 파싱).
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        var migrated = VaultMigrator.Migrate(root);
        return migrated.Deserialize<VaultData>(Options) ?? new VaultData();
    }
}
