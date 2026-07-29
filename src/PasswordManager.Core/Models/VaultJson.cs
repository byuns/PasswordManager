using System.Text.Json;
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

    public static VaultData Deserialize(string json) =>
        JsonSerializer.Deserialize<VaultData>(json, Options) ?? new VaultData();
}
