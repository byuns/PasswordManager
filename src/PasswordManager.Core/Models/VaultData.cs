namespace PasswordManager.Core.Models;

/// <summary>계정 목록 정렬 기준(TD-040). 즐겨찾기는 이 정렬과 무관하게 항상 맨 위에 온다.</summary>
public enum EntrySortOrder
{
    /// <summary>사이트명 가나다/알파벳순(기본).</summary>
    Name,

    /// <summary>비밀번호를 마지막으로 바꾼 순(최신 먼저).</summary>
    RecentlyChanged,

    /// <summary>마지막으로 인증해 쓴 순(최신 먼저).</summary>
    RecentlyUsed,
}

/// <summary>복호화된 볼트 본문의 최상위 구조. design 6. version은 마이그레이션 기준.</summary>
public sealed class VaultData
{
    /// <summary>현재 앱이 쓰는 볼트 스키마 버전. 구조 변경 시 올리고 <see cref="VaultMigrator"/>에 변환 단계를 추가한다(TD-008).</summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;
    public List<VaultEntry> Entries { get; set; } = new();

    /// <summary>앱 잠금해제 2FA용 TOTP secret(RFC4648 Base32). 미등록이면 null. design 5.4·TD-004.</summary>
    public string? AppTotpSecret { get; set; }

    /// <summary>유휴 자동 잠금까지의 분(design 5.5). 기본 5분. 구버전 볼트엔 없으므로 기본값이 유지된다.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>비밀번호 복사 후 클립보드 자동 삭제까지의 초(design 5.5). 기본 20초.</summary>
    public int ClipboardClearSeconds { get; set; } = 20;

    /// <summary>
    /// 전역 오프라인 스위치(TD-013). 기본 false = 아웃바운드 전면 차단. 슬랙은 이 스위치의 하위라,
    /// false면 슬랙 설정이 켜져 있어도 어떤 통신도 나가지 않는다. 구버전(v1) 볼트는 기본값 false 유지.
    /// </summary>
    public bool NetworkAllowed { get; set; }

    /// <summary>슬랙 알림 설정(옵트인·기본 OFF, TD-012). 구버전 볼트엔 없으므로 기본 인스턴스가 유지된다.</summary>
    public SlackSettings Slack { get; set; } = new();

    /// <summary>계정 목록 정렬 기준(TD-040). 구버전 볼트엔 없으므로 기본값(이름순)이 유지된다.</summary>
    public EntrySortOrder SortOrder { get; set; } = EntrySortOrder.Name;
}

/// <summary>슬랙 Incoming Webhook 알림 설정. secret(WebhookUrl)은 볼트 본문의 일부라 함께 암호화 저장된다. design 7.8·TD-012·TD-014.</summary>
public sealed class SlackSettings
{
    /// <summary>슬랙 알림 옵트인 여부(기본 OFF). 전역 <see cref="VaultData.NetworkAllowed"/>가 true여야 실제 동작.</summary>
    public bool Enabled { get; set; }

    /// <summary>Slack Incoming Webhook URL. 볼트 안에 암호화 저장(다른 secret과 동일 취급).</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>잠금 해제 성공 시 알림.</summary>
    public bool NotifyUnlock { get; set; } = true;

    /// <summary>마스터 비밀번호 로그인 실패 시 알림.</summary>
    public bool NotifyLoginFailure { get; set; } = true;

    /// <summary>비밀번호 추가·변경 시 알림.</summary>
    public bool NotifyPasswordChange { get; set; } = true;

    /// <summary>(선택) 마스터 비번 변경·복구 키 사용 등 민감 이벤트 알림(기본 OFF).</summary>
    public bool NotifySensitive { get; set; }

    /// <summary>사이트명 포함 여부. 기본 미포함(어떤 서비스를 쓰는지 노출 방지, design 7.8).</summary>
    public bool IncludeSiteName { get; set; }

    /// <summary>알림 메시지 템플릿. 허용 변수만 치환(TD-014). 기본값은 <see cref="DefaultTemplate"/>.</summary>
    public string MessageTemplate { get; set; } = DefaultTemplate;

    /// <summary>{기기명} 변수용 표시 이름(선택). null이면 빈 문자열로 치환.</summary>
    public string? DeviceName { get; set; }

    /// <summary>기본 메시지 템플릿(기본 복원 버튼용, design 7.8).</summary>
    public const string DefaultTemplate = "🔓 {이벤트} · {시각}";
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

    /// <summary>즐겨찾기 고정 여부(TD-040). 켜면 목록 최상단 "즐겨찾기" 그룹으로 모인다.</summary>
    public bool IsPinned { get; set; }

    /// <summary>마지막으로 OTP 인증해 쓴 시각(TD-040). 한 번도 안 썼으면 null. "최근 사용순" 정렬 기준.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// 휴지통에 들어간 시각(TD-041). null이면 활성 항목. 삭제는 이 값을 찍는 소프트 삭제이며,
    /// <see cref="Vault.VaultManager.TrashRetentionDays"/>가 지나면 언락 시 영구 삭제된다.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>이전 비밀번호 이력 한 건. 변경 시 적재하며 상한 관리. design 6·7.2.</summary>
public sealed class PasswordHistoryItem
{
    public string Password { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
}
