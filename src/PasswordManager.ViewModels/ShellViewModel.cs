using CommunityToolkit.Mvvm.ComponentModel;
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
        CurrentViewModel = vm;
        State = ShellState.Unlocking;
    }

    private void StartCreate()
    {
        var vm = new CreateVaultViewModel(_vault, _kdf);
        vm.Completed += OnVaultOpened;
        CurrentViewModel = vm;
        State = ShellState.Creating;
    }

    private void OnVaultOpened(object? sender, EventArgs e)
    {
        // 메인 화면 ViewModel은 후속 단계(항목 리스트)에서 연결한다.
        CurrentViewModel = null;
        State = ShellState.Open;
    }
}
