using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Notifications;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 설정 화면 ViewModel. 상단 툴바에 몰려 있던 보조 동작(OTP 등록/재설정 · 마스터 변경 ·
/// 백업/복원)을 이곳으로 모은다(design-ux 3절, S7). 실제 하위 화면 전환·파일 대화상자는
/// 이벤트로 셸/뷰에 위임하고, 여기서는 볼트 조작(백업/복원)만 직접 수행한다.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly VaultManager _vault;

    public SettingsViewModel(VaultManager vault)
    {
        _vault = vault;
        _autoLockMinutes = vault.AutoLockMinutes;
        _clipboardClearSeconds = vault.ClipboardClearSeconds;

        // 슬랙·네트워크 설정을 볼트에서 로드(design 7.8·7.9).
        var s = vault.Slack;
        _networkAllowed = vault.NetworkAllowed;
        _slackEnabled = s.Enabled;
        _slackWebhookUrl = s.WebhookUrl;
        _notifyUnlock = s.NotifyUnlock;
        _notifyLoginFailure = s.NotifyLoginFailure;
        _notifyPasswordChange = s.NotifyPasswordChange;
        _notifySensitive = s.NotifySensitive;
        _includeSiteName = s.IncludeSiteName;
        _messageTemplate = s.MessageTemplate;
        _deviceName = s.DeviceName ?? "";
    }

    /// <summary>사용자에게 보여줄 일시 안내(예: 백업 완료).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>유휴 자동 잠금까지의 분(design 5.5·7.9).</summary>
    [ObservableProperty]
    private int _autoLockMinutes;

    /// <summary>비밀번호 복사 후 클립보드 자동 삭제까지의 초(design 5.5·7.9).</summary>
    [ObservableProperty]
    private int _clipboardClearSeconds;

    /// <summary>시간 설정을 저장했을 때 발생. 셸이 구독해 실행 중인 자동잠금·클립보드에 즉시 반영한다.</summary>
    public event EventHandler? TimeSettingsChanged;

    // --- 네트워크·슬랙 설정 (design 7.8·7.9·TD-012·TD-013) ---

    /// <summary>전역 오프라인 스위치(기본 false=차단, TD-013). 이게 꺼져 있으면 슬랙 섹션은 비활성(회색).</summary>
    [ObservableProperty]
    private bool _networkAllowed;

    /// <summary>슬랙 알림 옵트인(기본 OFF).</summary>
    [ObservableProperty]
    private bool _slackEnabled;

    /// <summary>Slack Incoming Webhook URL(볼트 내 암호화 저장).</summary>
    [ObservableProperty]
    private string _slackWebhookUrl = "";

    /// <summary>잠금 해제 성공 알림.</summary>
    [ObservableProperty]
    private bool _notifyUnlock;

    /// <summary>로그인 실패 알림.</summary>
    [ObservableProperty]
    private bool _notifyLoginFailure;

    /// <summary>비밀번호 추가·변경 알림.</summary>
    [ObservableProperty]
    private bool _notifyPasswordChange;

    /// <summary>(선택) 마스터 변경·복구 키 사용 등 민감 이벤트 알림.</summary>
    [ObservableProperty]
    private bool _notifySensitive;

    /// <summary>사이트명 포함 여부(기본 미포함).</summary>
    [ObservableProperty]
    private bool _includeSiteName;

    /// <summary>알림 메시지 템플릿(허용 변수만 치환, TD-014).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplatePreview))]
    private string _messageTemplate = SlackSettings.DefaultTemplate;

    /// <summary>{기기명} 변수용 표시 이름(선택).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplatePreview))]
    private string _deviceName = "";

    /// <summary>미리보기용 고정 예시 시각(설정 화면 표시용, 실제 시각과 무관).</summary>
    private static readonly DateTimeOffset PreviewTime = new(2026, 7, 31, 14, 3, 0, TimeSpan.FromHours(9));

    /// <summary>현재 템플릿을 잠금 해제 이벤트·예시 값으로 렌더한 미리보기(TD-014).</summary>
    public string TemplatePreview =>
        SlackMessageTemplate.Render(MessageTemplate, SlackEvent.Unlock, PreviewTime,
            string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName);

    /// <summary>네트워크·슬랙 설정을 저장했을 때 발생. 셸이 구독해 세션 캐시(TD-037)를 갱신한다.</summary>
    public event EventHandler? NetworkSettingsChanged;

    /// <summary>앱 잠금해제 OTP가 등록되어 있는가(상태 표시·재설정 안내용).</summary>
    public bool IsOtpRegistered => _vault.HasOtp;

    /// <summary>OTP 등록 상태를 사람이 읽는 문자열로(설정 카드 표시용).</summary>
    public string OtpStatusText => IsOtpRegistered ? "등록됨" : "미등록";

    /// <summary>OTP 액션 버튼 문구. 미등록이면 "등록", 등록됨이면 "재설정"(마스터 재확인 동반).</summary>
    public string OtpActionLabel => IsOtpRegistered ? "재설정" : "등록";

    /// <summary>OTP 등록(재설정) 요청. 셸이 등록 마법사를 연다.</summary>
    public event EventHandler? OtpSetupRequested;

    /// <summary>마스터 비밀번호 변경 요청. 셸이 변경 화면을 연다.</summary>
    public event EventHandler? ChangeMasterRequested;

    /// <summary>복구 키 재발급 요청. 셸이 재발급 화면을 연다(design 7.6).</summary>
    public event EventHandler? ReissueRecoveryRequested;

    /// <summary>백업 요청. 뷰가 저장 위치 대화상자를 연다.</summary>
    public event EventHandler? BackupRequested;

    /// <summary>복원 요청. 뷰가 백업 파일 선택 대화상자를 연다.</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>내보내기 요청. 뷰가 저장 위치 대화상자를 연다(평문 CSV — 경고 후).</summary>
    public event EventHandler? ExportRequested;

    /// <summary>가져오기 요청. 뷰가 CSV 파일 선택 대화상자를 연다.</summary>
    public event EventHandler? ImportRequested;

    /// <summary>복원으로 세션이 닫혔을 때 발생. 셸이 언락 화면으로 돌아간다.</summary>
    public event EventHandler? Locked;

    /// <summary>OTP 등록 상태 등 볼트에서 읽는 값을 다시 반영한다(하위 화면에서 복귀 시).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOtpRegistered));
        OnPropertyChanged(nameof(OtpStatusText));
        OnPropertyChanged(nameof(OtpActionLabel));
    }

    [RelayCommand]
    private void SetupOtp() => OtpSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChangeMasterPassword() => ChangeMasterRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ReissueRecovery() => ReissueRecoveryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Backup() => BackupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Export() => ExportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Import() => ImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>시간 설정을 허용 범위로 보정해 저장하고, 실행 중인 컴포넌트에 반영하도록 알린다.</summary>
    [RelayCommand]
    private void SaveTimeSettings()
    {
        AutoLockMinutes = Math.Clamp(AutoLockMinutes, 1, 120);       // 1분 ~ 2시간
        ClipboardClearSeconds = Math.Clamp(ClipboardClearSeconds, 5, 300); // 5초 ~ 5분
        _vault.SetTimeSettings(AutoLockMinutes, ClipboardClearSeconds);
        StatusMessage = "시간 설정을 저장했습니다.";
        TimeSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>네트워크·슬랙 설정을 볼트에 저장하고, 세션 캐시 갱신을 알린다(design 7.8·TD-037).</summary>
    [RelayCommand]
    private void SaveSlackSettings()
    {
        var slack = new SlackSettings
        {
            Enabled = SlackEnabled,
            WebhookUrl = SlackWebhookUrl?.Trim() ?? "",
            NotifyUnlock = NotifyUnlock,
            NotifyLoginFailure = NotifyLoginFailure,
            NotifyPasswordChange = NotifyPasswordChange,
            NotifySensitive = NotifySensitive,
            IncludeSiteName = IncludeSiteName,
            MessageTemplate = string.IsNullOrWhiteSpace(MessageTemplate)
                ? SlackSettings.DefaultTemplate : MessageTemplate,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? null : DeviceName.Trim(),
        };
        _vault.SaveNetworkSettings(NetworkAllowed, slack);
        StatusMessage = "알림 설정을 저장했습니다.";
        NetworkSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>메시지 템플릿을 기본값으로 되돌린다(design 7.8). 저장은 별도 버튼으로.</summary>
    [RelayCommand]
    private void ResetTemplate() => MessageTemplate = SlackSettings.DefaultTemplate;

    /// <summary>현재 항목을 CSV(평문)로 만들어 돌려준다. 뷰가 경고 후 파일에 쓴다(design 7.7).</summary>
    public string BuildExportCsv() => _vault.ExportCsv();

    /// <summary>뷰가 읽어온 CSV 텍스트로 항목을 가져오고(모두 새 항목 추가) 개수를 돌려준다.</summary>
    public int PerformImport(string csv)
    {
        var count = _vault.ImportCsv(csv);
        StatusMessage = $"{count}개 계정을 가져왔습니다.";
        return count;
    }

    /// <summary>선택한 경로로 볼트를 백업한다(뷰가 대화상자에서 경로를 받아 호출).</summary>
    public void PerformBackup(string path)
    {
        _vault.Backup(path);
        StatusMessage = "백업을 완료했습니다.";
    }

    /// <summary>백업 파일로 복원하고 잠금 화면으로 돌아간다(백업 시점의 마스터 비번으로 재로그인).</summary>
    public void PerformRestore(string path)
    {
        _vault.Restore(path);                  // 세션 닫힘
        Locked?.Invoke(this, EventArgs.Empty); // 언락 화면으로 전환
    }
}
