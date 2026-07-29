using CommunityToolkit.Mvvm.ComponentModel;
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

/// <summary>
/// 앱의 최상위 셸 ViewModel. 최초 실행 여부(<see cref="VaultManager.Exists"/>)에 따라
/// 생성/언락 화면을 띄우고, 성공하면 열림 상태로 전환한다. 활성 화면은
/// <see cref="CurrentViewModel"/>에 담아 View가 DataTemplate로 렌더링한다.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly KdfParams _kdf;
    private MainViewModel? _main;

    [ObservableProperty]
    private ShellState _state;

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    public ShellViewModel(VaultManager vault, KdfParams? kdf = null)
    {
        _vault = vault;
        _kdf = kdf ?? KdfParams.Recommended;

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
        State = ShellState.Unlocking;
    }

    private void OnRecoveryRequested(object? sender, EventArgs e)
    {
        var vm = new RecoveryViewModel(_vault, _kdf);
        vm.Recovered += OnVaultOpened;
        vm.Cancelled += OnRecoveryCancelled;
        CurrentViewModel = vm;
        State = ShellState.Unlocking;
    }

    private void OnRecoveryCancelled(object? sender, EventArgs e) => StartUnlock();

    private void StartCreate()
    {
        var vm = new CreateVaultViewModel(_vault, _kdf);
        vm.Completed += OnVaultOpened;
        CurrentViewModel = vm;
        State = ShellState.Creating;
    }

    private void OnVaultOpened(object? sender, EventArgs e)
    {
        ShowMain();
        State = ShellState.Open;
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
            _main.ChangeMasterRequested += OnChangeMasterRequested;
            _main.OtpSetupRequested += OnOtpSetupRequested;
            _main.RevealRequested += OnRevealRequested;
        }
        else
        {
            _main.Refresh();
        }
        CurrentViewModel = _main;
    }

    private void OnLocked(object? sender, EventArgs e)
    {
        if (_main is not null)
        {
            _main.Locked -= OnLocked;
            _main.AddRequested -= OnAddRequested;
            _main.EditRequested -= OnEditRequested;
            _main.ChangeMasterRequested -= OnChangeMasterRequested;
            _main.OtpSetupRequested -= OnOtpSetupRequested;
            _main.RevealRequested -= OnRevealRequested;
            _main = null;
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
    }

    private void OnChangeMasterRequested(object? sender, EventArgs e)
    {
        var vm = new ChangeMasterPasswordViewModel(_vault, _kdf);
        vm.Changed += OnChangeMasterFinished;
        vm.Cancelled += OnChangeMasterFinished;
        CurrentViewModel = vm;
    }

    private void OnChangeMasterFinished(object? sender, EventArgs e)
    {
        if (sender is ChangeMasterPasswordViewModel vm)
        {
            vm.Changed -= OnChangeMasterFinished;
            vm.Cancelled -= OnChangeMasterFinished;
        }
        ShowMain();
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
    }

    private void OnOtpSetupFinished(object? sender, EventArgs e)
    {
        if (sender is OtpSetupViewModel vm)
        {
            vm.Completed -= OnOtpSetupFinished;
            vm.Cancelled -= OnOtpSetupFinished;
        }
        ShowMain();
    }

    private void OnRevealRequested(object? sender, VaultEntry entry)
    {
        // 검증 성공 시 게이트 화면이 그 자리에서 비밀번호를 보여주고, 닫기(취소)로 메인에 복귀한다.
        var vm = new OtpGateViewModel(_vault, entry);
        vm.Cancelled += OnGateClosed;
        CurrentViewModel = vm;
    }

    private void OnGateClosed(object? sender, EventArgs e)
    {
        if (sender is OtpGateViewModel vm)
            vm.Cancelled -= OnGateClosed;
        ShowMain();
    }
}
