namespace PasswordManager.Core.Models;

/// <summary>복호화된 볼트 본문의 최상위 구조. design 6. version은 마이그레이션 기준.</summary>
public sealed class VaultData
{
    public int Version { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = new();
}

/// <summary>계정 항목 하나. 같은 사이트의 여러 계정은 각각 독립 Entry. design 6.</summary>
public sealed class VaultEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastChangedAt { get; set; }
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = new();
    public string? TotpSecret { get; set; }
}

/// <summary>이전 비밀번호 이력 한 건. 변경 시 적재하며 상한 관리. design 6·7.2.</summary>
public sealed class PasswordHistoryItem
{
    public string Password { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
}
