namespace PasswordManager.Core.Vault;

/// <summary>
/// 전체 데이터 초기화가 지울 파일 목록을 정한다 (TD-044). 실제 삭제는 파일 I/O 계층이 맡고,
/// 여기서는 "무엇을 지울지"라는 규칙만 순수 함수로 둬 테스트할 수 있게 한다.
/// 볼트 본체뿐 아니라 앱이 볼트 옆에 만드는 사이드카(자동 백업·잠금 상태·로그)까지 모두 포함한다 —
/// 하나라도 남으면 "첫 설치 상태로 되돌린다"는 목적이 깨진다.
/// </summary>
public static class VaultReset
{
    /// <summary>원자적 쓰기 도중 남을 수 있는 임시 파일 확장자(<see cref="Storage"/> 계층 규칙).</summary>
    public const string TempSuffix = ".tmp";

    /// <summary>저장 직전 이전 파일을 보존하는 자동 백업 확장자(design 5.3).</summary>
    public const string BackupSuffix = ".bak";

    /// <summary>로그인 재시도 잠금 상태 사이드카 확장자(TD-024).</summary>
    public const string LockoutSuffix = ".lockout";

    /// <summary>슬랙 전송 실패 로그 파일명. 볼트와 같은 폴더에 놓인다(design 7.8).</summary>
    public const string SlackFailureLogName = "slack-failures.log";

    /// <summary>
    /// 초기화 시 지울 경로들을 볼트 경로로부터 만든다. 실제로 존재하는지는 확인하지 않으므로,
    /// 호출부가 존재하는 것만 골라 지우면 된다.
    /// </summary>
    public static IReadOnlyList<string> PathsFor(string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new ArgumentException("볼트 경로가 비어 있습니다.", nameof(vaultPath));

        var folder = Path.GetDirectoryName(vaultPath);
        var slackLog = string.IsNullOrEmpty(folder)
            ? SlackFailureLogName
            : Path.Combine(folder, SlackFailureLogName);

        return new[]
        {
            vaultPath,
            vaultPath + TempSuffix,
            vaultPath + BackupSuffix,
            vaultPath + LockoutSuffix,
            slackLog,
        };
    }
}
