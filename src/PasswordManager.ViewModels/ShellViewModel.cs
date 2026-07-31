using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Notifications;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels;

/// <summary>셸이 보여주는 최상위 화면 단계.</summary>
public enum ShellState
{
    /// <summary>최초 실행 — 새 볼트 생성.</summary>
    Creating,
    /// <summary>기존 볼트 언락 대기.</summary>
    Unlocking,
    /// <summary>볼트가 열려 메인을 보여주는 상태.</summary>
    Open,
}

/// <summary>열림 모드 사이드바가 가리키는 최상위 섹션(design-ux 3절).</summary>
public enum ShellSection
{
    /// <summary>항목(카드 리스트).</summary>
    Items,
    /// <summary>설정.</summary>
    Settings,
    /// <summary>정보.</summary>
    Info,
}

/// <summary>
/// 앱의 최상위 셸 ViewModel. 최초 실행 여부(<see cref="VaultManager.Exists"/>)에 따라
/// 생성/언락 화면을 띄우고, 성공하면 열림 상태로 전환한다. 활성 화면은
/// <see cref="CurrentViewModel"/>에 담아 View가 DataTemplate로 렌더링한다.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly KdfParams _kdf;
    private readonly ClipboardCopier? _clipboard;
    private readonly LoginThrottle? _throttle;
    private readonly string? _appVersion;
    private readonly string? _vaultPath;
    private readonly ISlackNotifier? _slack;       // 슬랙 알림 조율기(옵션, 없으면 알림 미동작)
    private readonly SlackConfigCache? _slackCache; // 로그인 실패 알림용 세션 캐시(A안)
    private MainViewModel? _main;
    private SettingsViewModel? _settings;
    private InfoViewModel? _info;

    /// <summary>OTP 등록 화면을 메인(항목 목록)에서 열었는지. true면 완료·취소 후 메인으로, 아니면 설정으로 복귀.</summary>
    private bool _otpSetupFromMain;

    /// <summary>이번 세션에 OTP 게이트를 통과한 항목 ID. 한 번 통과하면 그 항목의 보기·편집을
    /// 코드 없이 연다(TD-004). 볼트가 잠기면 비운다.</summary>
    private readonly HashSet<string> _otpVerified = new();

    [ObservableProperty]
    private ShellState _state;

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    /// <summary>
    /// 열림 모드에서 사이드바가 가리키는 섹션. 잠금 상태·하위 흐름(편집·게이트 등
    /// 집중 작업)에서는 <c>null</c>이며, 이때 사이드바를 숨긴다.
    /// </summary>
    [ObservableProperty]
    private ShellSection? _section;

    /// <summary>열림 모드의 최상위 페이지를 보여줄 때만 사이드바를 노출한다.</summary>
    public bool IsSidebarVisible => Section is not null;

    /// <summary>확인창·완료 토스트를 담당하는 다이얼로그 서비스. 창이 뜬 뒤 App/View가 주입한다(그 전엔 null).</summary>
    public IDialogService? Dialog { get; set; }

    partial void OnSectionChanged(ShellSection? value) =>
        OnPropertyChanged(nameof(IsSidebarVisible));

    public ShellViewModel(VaultManager vault, KdfParams? kdf = null, ClipboardCopier? clipboard = null,
        string? appVersion = null, string? vaultPath = null, LoginThrottle? throttle = null,
        ISlackNotifier? slack = null, SlackConfigCache? slackCache = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;
        _clipboard = clipboard;
        _throttle = throttle;
        _appVersion = appVersion;
        _vaultPath = vaultPath;
        _slack = slack;
        _slackCache = slackCache;

        if (_vault.Exists())
            StartUnlock();
        else
            StartCreate();
    }

    private void StartUnlock()
    {
        var vm = new UnlockViewModel(_vault, _throttle);
        vm.Unlocked += OnVaultOpened;
        vm.LoginFailed += OnLoginFailed;
        vm.RecoveryRequested += OnRecoveryRequested;
        CurrentViewModel = vm;
        Section = null;
        State = ShellState.Unlocking;
    }

    /// <summary>마스터 비번 로그인 실패 → 세션 캐시(A안)의 마지막 설정으로 슬랙 알림(design 7.8).</summary>
    private void OnLoginFailed(object? sender, EventArgs e) => FireSlack(SlackEvent.LoginFailure);

    /// <summary>잠금 해제·설정 저장 시 슬랙 설정 스냅샷을 세션 캐시에 갱신한다(로그인 실패 알림용).</summary>
    public void RefreshSlackConfig()
    {
        if (_slackCache is null || !_vault.IsUnlocked) return;
        _slackCache.Update(new SlackConfig(_vault.NetworkAllowed, _vault.Slack));
    }

    /// <summary>슬랙 알림을 베스트에포트로 쏜다(실패·미설정이어도 앱 흐름을 막지 않음).</summary>
    private void FireSlack(SlackEvent kind, string? siteName = null)
    {
        if (_slack is null) return;
        _ = _slack.NotifyAsync(kind, DateTimeOffset.Now, siteName);
    }

    private void OnRecoveryRequested(object? sender, EventArgs e)
    {
        var vm = new RecoveryViewModel(_vault, _kdf);
        vm.Recovered += OnVaultOpened;
        vm.Cancelled += OnRecoveryCancelled;
        CurrentViewModel = vm;
        Section = null;
        State = ShellState.Unlocking;
    }

    private void OnRecoveryCancelled(object? sender, EventArgs e) => StartUnlock();

    private void OnTimeSettingsChanged(object? sender, EventArgs e) => ApplyClipboardDelay();

    /// <summary>슬랙·네트워크 설정 저장 → 세션 캐시(TD-037)를 새 값으로 갱신해 이후 알림에 반영.</summary>
    private void OnNetworkSettingsChanged(object? sender, EventArgs e) => RefreshSlackConfig();

    private void StartCreate()
    {
        var vm = new CreateVaultViewModel(_vault, _kdf);
        vm.Completed += OnVaultOpened;
        CurrentViewModel = vm;
        Section = null;
        State = ShellState.Creating;
    }

    private void OnVaultOpened(object? sender, EventArgs e)
    {
        ApplyClipboardDelay();
        RefreshSlackConfig();          // 세션 캐시에 설정 반영(이후 로그인 실패 알림용)
        FireSlack(SlackEvent.Unlock);  // 잠금 해제 성공 알림(design 7.8)
        ShowMain();
        State = ShellState.Open;
    }

    /// <summary>유휴 자동 잠금까지의 시간(볼트 설정 기반). 잠금 상태에선 기본 5분. ShellWindow 폴링이 읽는다.</summary>
    public TimeSpan AutoLockTimeout =>
        _vault.IsUnlocked ? TimeSpan.FromMinutes(_vault.AutoLockMinutes) : TimeSpan.FromMinutes(5);

    /// <summary>볼트에 저장된 클립보드 삭제 시간을 실행 중인 ClipboardCopier에 반영한다(design 5.5·7.9).</summary>
    private void ApplyClipboardDelay()
    {
        if (_clipboard is not null && _vault.IsUnlocked)
            _clipboard.ClearDelay = TimeSpan.FromSeconds(_vault.ClipboardClearSeconds);
    }

    /// <summary>메인 화면을 띄운다. 최초 1회 MainViewModel을 만들고, 재진입 시엔 목록만 갱신한다.</summary>
    private void ShowMain()
    {
        if (_main is null)
        {
            _main = new MainViewModel(_vault, _clipboard, _otpVerified, Dialog);
            _main.Locked += OnLocked;
            _main.AddRequested += OnAddRequested;
            _main.EditRequested += OnEditRequested;
            _main.RevealRequested += OnRevealRequested;
            _main.VerifyRequested += OnVerifyRequested;
            _main.OtpSetupRequested += OnMainOtpSetupRequested;
        }
        else
        {
            _main.Refresh();
        }
        CurrentViewModel = _main;
        Section = ShellSection.Items;
    }

    // --- 사이드바 네비게이션 (열림 모드, design-ux 3절) ---

    /// <summary>항목(메인) 섹션으로 이동한다.</summary>
    [RelayCommand]
    private void ShowItems() => ShowMain();

    /// <summary>설정 섹션으로 이동한다. 최초 1회 SettingsViewModel을 만들며 보조 동작 이벤트를 배선한다.</summary>
    [RelayCommand]
    private void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsViewModel(_vault);
            _settings.OtpSetupRequested += OnOtpSetupRequested;
            _settings.ChangeMasterRequested += OnChangeMasterRequested;
            _settings.ReissueRecoveryRequested += OnReissueRecoveryRequested;
            _settings.Locked += OnLocked; // 복원 시 세션이 닫히면 언락 화면으로
            _settings.TimeSettingsChanged += OnTimeSettingsChanged;
            _settings.NetworkSettingsChanged += OnNetworkSettingsChanged;
        }
        else
        {
            _settings.Refresh(); // OTP 등록 상태 등 갱신
        }
        CurrentViewModel = _settings;
        Section = ShellSection.Settings;
    }

    /// <summary>정보 섹션으로 이동한다.</summary>
    [RelayCommand]
    private void ShowInfo()
    {
        _info ??= new InfoViewModel(version: _appVersion, vaultPath: _vaultPath);
        CurrentViewModel = _info;
        Section = ShellSection.Info;
    }

    /// <summary>사이드바 잠금 버튼. 열려 있을 때만 볼트를 잠그고 언락 화면으로 돌아간다.</summary>
    [RelayCommand]
    private void Lock() => AutoLock();

    /// <summary>유휴 자동 잠금(design 5.5). 열려 있을 때만 볼트를 잠그고 언락 화면으로 돌아간다.</summary>
    public void AutoLock()
    {
        if (State != ShellState.Open) return;
        _vault.Lock();
        OnLocked(this, EventArgs.Empty);
    }

    private void OnLocked(object? sender, EventArgs e)
    {
        _otpVerified.Clear(); // 잠기면 그레이스 해제 — 다음 열람·편집은 다시 OTP를 요구
        if (_main is not null)
        {
            _main.Locked -= OnLocked;
            _main.AddRequested -= OnAddRequested;
            _main.EditRequested -= OnEditRequested;
            _main.RevealRequested -= OnRevealRequested;
            _main.VerifyRequested -= OnVerifyRequested;
            _main.OtpSetupRequested -= OnMainOtpSetupRequested;
            _main = null;
        }
        if (_settings is not null)
        {
            _settings.OtpSetupRequested -= OnOtpSetupRequested;
            _settings.ChangeMasterRequested -= OnChangeMasterRequested;
            _settings.Locked -= OnLocked;
            _settings.TimeSettingsChanged -= OnTimeSettingsChanged;
            _settings = null;
        }
        StartUnlock();
    }

    private void OnAddRequested(object? sender, EventArgs e) =>
        ShowEditor(new EntryEditViewModel(_vault));

    private void OnEditRequested(object? sender, VaultEntry entry)
    {
        // 편집도 기존 비번을 노출하므로 게이트를 거친다(TD-004). 이번 세션에 이미 통과한 항목은 바로 연다.
        if (_otpVerified.Contains(entry.Id))
        {
            ShowEditor(new EntryEditViewModel(_vault, entry));
            return;
        }

        var gate = new OtpGateViewModel(_vault, entry, copier: _clipboard, purpose: OtpGatePurpose.Edit);
        void OnVerified(object? s, EventArgs e)
        {
            gate.Verified -= OnVerified;
            gate.Cancelled -= OnCancelled;
            _otpVerified.Add(entry.Id);
            ShowEditor(new EntryEditViewModel(_vault, entry));
        }
        void OnCancelled(object? s, EventArgs e)
        {
            gate.Verified -= OnVerified;
            gate.Cancelled -= OnCancelled;
            ShowMain();
        }
        gate.Verified += OnVerified;
        gate.Cancelled += OnCancelled;
        CurrentViewModel = gate;
        Section = null;
    }

    private void OnMainOtpSetupRequested(object? sender, EventArgs e)
    {
        // 메인의 미등록 행 버튼('OTP 등록')에서 진입 → 등록 후 메인으로 복귀.
        _otpSetupFromMain = true;
        OpenOtpSetup();
    }

    private void OnVerifyRequested(object? sender, VaultEntry entry)
    {
        // 이미 이번 세션에 통과했으면(그레이스) 바로 목록으로(버튼은 이미 3개 상태).
        if (_otpVerified.Contains(entry.Id))
        {
            ShowMain();
            return;
        }

        var gate = new OtpGateViewModel(_vault, entry, copier: _clipboard, purpose: OtpGatePurpose.Verify);
        void OnVerified(object? s, EventArgs e)
        {
            gate.Verified -= OnVerified;
            gate.Cancelled -= OnCancelled;
            _otpVerified.Add(entry.Id); // 세션 그레이스: 이 항목의 보기·편집·삭제가 열린다
            ShowMain();
        }
        void OnCancelled(object? s, EventArgs e)
        {
            gate.Verified -= OnVerified;
            gate.Cancelled -= OnCancelled;
            ShowMain();
        }
        gate.Verified += OnVerified;
        gate.Cancelled += OnCancelled;
        CurrentViewModel = gate;
        Section = null;
    }

    private void ShowEditor(EntryEditViewModel editor)
    {
        editor.Saved += OnEditorSaved;
        editor.Cancelled += OnEditorCancelled;
        CurrentViewModel = editor;
        Section = null;
    }

    private void OnChangeMasterRequested(object? sender, EventArgs e)
    {
        var vm = new ChangeMasterPasswordViewModel(_vault, _kdf);
        vm.Changed += OnChangeMasterFinished;
        vm.Cancelled += OnChangeMasterFinished;
        CurrentViewModel = vm;
        Section = null;
    }

    private void OnChangeMasterFinished(object? sender, EventArgs e)
    {
        if (sender is ChangeMasterPasswordViewModel vm)
        {
            vm.Changed -= OnChangeMasterFinished;
            vm.Cancelled -= OnChangeMasterFinished;
        }
        ShowSettings(); // 설정에서 진입했으므로 설정으로 복귀
    }

    private void OnReissueRecoveryRequested(object? sender, EventArgs e)
    {
        var vm = new ReissueRecoveryKeyViewModel(_vault, _kdf);
        vm.Completed += OnReissueRecoveryFinished;
        vm.Cancelled += OnReissueRecoveryFinished;
        CurrentViewModel = vm;
        Section = null;
    }

    private void OnReissueRecoveryFinished(object? sender, EventArgs e)
    {
        if (sender is ReissueRecoveryKeyViewModel vm)
        {
            vm.Completed -= OnReissueRecoveryFinished;
            vm.Cancelled -= OnReissueRecoveryFinished;
        }
        ShowSettings(); // 설정에서 진입했으므로 설정으로 복귀
    }

    private void OnEditorSaved(object? sender, EventArgs e)
    {
        // 비밀번호를 새로 추가·변경한 저장만 슬랙에 알린다(메타데이터만 바뀐 저장은 제외, design 7.8).
        if (sender is EntryEditViewModel editor && editor.LastSaveChangedPassword)
            FireSlack(SlackEvent.PasswordChange, editor.Title);

        DetachEditor(sender);
        ShowMain();
        Dialog?.Notify("저장됨", "변경 내용을 저장했습니다.");
    }

    private void OnEditorCancelled(object? sender, EventArgs e)
    {
        DetachEditor(sender);
        ShowMain();
    }

    private void DetachEditor(object? sender)
    {
        if (sender is EntryEditViewModel editor)
        {
            editor.Saved -= OnEditorSaved;
            editor.Cancelled -= OnEditorCancelled;
        }
    }

    private void OnOtpSetupRequested(object? sender, EventArgs e)
    {
        _otpSetupFromMain = false; // 설정에서 진입 → 완료 후 설정으로 복귀
        // 재설정(이미 등록됨)은 마스터 비번 재확인을 거친다(TD-005). 최초 등록은 방금 언락했으므로 바로 진행.
        if (_vault.HasOtp)
        {
            var confirm = new MasterConfirmViewModel(_vault, "OTP를 재설정하려면 마스터 비밀번호를 확인하세요.");
            confirm.Confirmed += OnOtpResetConfirmed;
            confirm.Cancelled += OnOtpResetCancelled;
            CurrentViewModel = confirm;
            Section = null;
        }
        else
        {
            OpenOtpSetup();
        }
    }

    private void OnOtpResetConfirmed(object? sender, EventArgs e)
    {
        DetachMasterConfirm(sender);
        OpenOtpSetup();
    }

    private void OnOtpResetCancelled(object? sender, EventArgs e)
    {
        DetachMasterConfirm(sender);
        ShowSettings();
    }

    private void DetachMasterConfirm(object? sender)
    {
        if (sender is MasterConfirmViewModel vm)
        {
            vm.Confirmed -= OnOtpResetConfirmed;
            vm.Cancelled -= OnOtpResetCancelled;
        }
    }

    private void OpenOtpSetup()
    {
        var vm = new OtpSetupViewModel(_vault);
        vm.Completed += OnOtpSetupFinished;
        vm.Cancelled += OnOtpSetupFinished;
        CurrentViewModel = vm;
        Section = null;
    }

    private void OnOtpSetupFinished(object? sender, EventArgs e)
    {
        if (sender is OtpSetupViewModel vm)
        {
            vm.Completed -= OnOtpSetupFinished;
            vm.Cancelled -= OnOtpSetupFinished;
        }
        // 진입한 곳으로 복귀(메인이면 목록 갱신 → 등록됐으면 행 버튼이 '인증'으로 전환).
        if (_otpSetupFromMain)
        {
            _otpSetupFromMain = false;
            ShowMain();
        }
        else
        {
            ShowSettings();
        }
    }

    private void OnRevealRequested(object? sender, VaultEntry entry)
    {
        // 검증 성공 시 게이트 화면이 그 자리에서 비밀번호를 보여주고, 닫기(취소)로 메인에 복귀한다.
        // 이번 세션에 이미 통과한 항목은 코드 없이 즉시 노출한다(그레이스).
        bool granted = _otpVerified.Contains(entry.Id);
        var gate = new OtpGateViewModel(_vault, entry, copier: _clipboard,
            purpose: OtpGatePurpose.Reveal, preVerified: granted);
        void OnRevealed(object? s, EventArgs e) => _otpVerified.Add(entry.Id);
        void OnClosed(object? s, EventArgs e)
        {
            gate.Revealed -= OnRevealed;
            gate.Cancelled -= OnClosed;
            ShowMain();
        }
        gate.Revealed += OnRevealed;
        gate.Cancelled += OnClosed;
        CurrentViewModel = gate;
        Section = null;
    }
}
