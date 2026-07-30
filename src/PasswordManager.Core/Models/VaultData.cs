namespace PasswordManager.Core.Models;

/// <summary>복호화된 볼트 본문의 최상위 구조. design 6. version은 마이그레이션 기준.</summary>
public sealed class VaultData
{
    /// <summary>현재 앱이 쓰는 볼트 스키마 버전. 구조 변경 시 올리고 <see cref="VaultMigrator"/>에 변환 단계를 추가한다(TD-008).</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<VaultEntry> Entries { get; set; } = new();

    /// <summary>앱 잠금해제 2FA용 TOTP secret(RFC4648 Base32). 미등록이면 null. design 5.4·TD-004.</summary>
    public string? AppTotpSecret { get; set; }

    /// <summary>유휴 자동 잠금까지의 분(design 5.5). 기본 5분. 구버전 볼트엔 없으므로 기본값이 유지된다.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>비밀번호 복사 후 클립보드 자동 삭제까지의 초(design 5.5). 기본 20초.</summary>
    public int ClipboardClearSeconds { get; set; } = 20;
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
