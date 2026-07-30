using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

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
    private readonly string? _appVersion;
    private readonly string? _vaultPath;
    private MainViewModel? _main;
    private SettingsViewModel? _settings;
    private InfoViewModel? _info;

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

    partial void OnSectionChanged(ShellSection? value) =>
        OnPropertyChanged(nameof(IsSidebarVisible));

    public ShellViewModel(VaultManager vault, KdfParams? kdf = null, ClipboardCopier? clipboard = null,
        string? appVersion = null, string? vaultPath = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;
        _clipboard = clipboard;
        _appVersion = appVersion;
        _vaultPath = vaultPath;

        if (_vault.Exists())
            StartUnlock();
        else
            StartCreate();
    }

    private void StartUnlock()
    {
        var vm = new UnlockViewModel(_vault);
        vm.Unlocked += OnVaultOpened;
        vm.RecoveryRequested += OnRecoveryRequested;
        CurrentViewModel = vm;
        Section = null;
        State = ShellState.Unlocking;
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
            _main = new MainViewModel(_vault);
            _main.Locked += OnLocked;
            _main.AddRequested += OnAddRequested;
            _main.EditRequested += OnEditRequested;
            _main.RevealRequested += OnRevealRequested;
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
            _settings.Locked += OnLocked; // 복원 시 세션이 닫히면 언락 화면으로
            _settings.TimeSettingsChanged += OnTimeSettingsChanged;
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
        if (_main is not null)
        {
            _main.Locked -= OnLocked;
            _main.AddRequested -= OnAddRequested;
            _main.EditRequested -= OnEditRequested;
            _main.RevealRequested -= OnRevealRequested;
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

    private void OnEditRequested(object? sender, VaultEntry entry) =>
        ShowEditor(new EntryEditViewModel(_vault, entry));

    private void ShowEditor(EntryEditViewModel editor)
    {
        editor.Saved += OnEditorFinished;
        editor.Cancelled += OnEditorFinished;
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

    private void OnEditorFinished(object? sender, EventArgs e)
    {
        if (sender is EntryEditViewModel editor)
        {
            editor.Saved -= OnEditorFinished;
            editor.Cancelled -= OnEditorFinished;
        }
        ShowMain();
    }

    private void OnOtpSetupRequested(object? sender, EventArgs e)
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
        ShowSettings(); // 설정에서 진입했으므로 설정으로 복귀(등록 상태 갱신)
    }

    private void OnRevealRequested(object? sender, VaultEntry entry)
    {
        // 검증 성공 시 게이트 화면이 그 자리에서 비밀번호를 보여주고, 닫기(취소)로 메인에 복귀한다.
        var vm = new OtpGateViewModel(_vault, entry, copier: _clipboard);
        vm.Cancelled += OnGateClosed;
        CurrentViewModel = vm;
        Section = null;
    }

    private void OnGateClosed(object? sender, EventArgs e)
    {
        if (sender is OtpGateViewModel vm)
            vm.Cancelled -= OnGateClosed;
        ShowMain();
    }
}
