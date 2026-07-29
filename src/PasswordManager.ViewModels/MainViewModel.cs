using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 언락 후 메인 화면 ViewModel. 항목 목록을 보여주고 제목·로그인으로 검색하며,
/// 선택 항목의 편집/삭제와 볼트 잠금을 처리한다. 추가/편집은 이벤트로 셸에 위임한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly VaultManager _vault;

    public MainViewModel(VaultManager vault)
    {
        _vault = vault;
        Refresh();
    }

    /// <summary>현재 화면에 보이는(검색 필터가 적용된) 항목들.</summary>
    public ObservableCollection<VaultEntry> Entries { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevealCommand))]
    private VaultEntry? _selectedEntry;

    /// <summary>사용자에게 보여줄 일시 안내(예: OTP 미등록 시 열람 안내). design 5.4.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>앱 잠금해제 OTP가 등록되어 있는가(열람 게이트 사용 여부, 등록 버튼 표시용).</summary>
    public bool IsOtpRegistered => _vault.HasOtp;

    /// <summary>볼트가 잠겼을 때 발생. 셸이 구독해 언락 화면으로 돌아간다.</summary>
    public event EventHandler? Locked;

    /// <summary>새 항목 추가 요청. 셸이 편집 화면을 연다.</summary>
    public event EventHandler? AddRequested;

    /// <summary>선택 항목 편집 요청. 셸이 편집 화면을 연다.</summary>
    public event EventHandler<VaultEntry>? EditRequested;

    /// <summary>마스터 비밀번호 변경 요청. 셸이 변경 화면을 연다.</summary>
    public event EventHandler? ChangeMasterRequested;

    /// <summary>OTP 등록(재설정) 요청. 셸이 등록 마법사를 연다.</summary>
    public event EventHandler? OtpSetupRequested;

    /// <summary>선택 항목 비밀번호 열람 요청. 셸이 OTP 게이트를 연다(design 7.4).</summary>
    public event EventHandler<VaultEntry>? RevealRequested;

    /// <summary>백업 요청. 뷰가 저장 위치 대화상자를 연다(M6).</summary>
    public event EventHandler? BackupRequested;

    /// <summary>복원 요청. 뷰가 백업 파일 선택 대화상자를 연다(M6).</summary>
    public event EventHandler? RestoreRequested;

    partial void OnSearchQueryChanged(string value) => Refresh();

    /// <summary>볼트에서 항목을 다시 읽어 검색 필터를 적용해 목록을 갱신한다.</summary>
    public void Refresh()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        IEnumerable<VaultEntry> source = _vault.Entries;
        if (query.Length > 0)
            source = source.Where(e =>
                e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Login.Contains(query, StringComparison.OrdinalIgnoreCase));

        Entries.Clear();
        foreach (var entry in source)
            Entries.Add(entry);

        OnPropertyChanged(nameof(IsOtpRegistered));
    }

    private bool HasSelection() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        _vault.Remove(SelectedEntry!.Id);
        SelectedEntry = null;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit() => EditRequested?.Invoke(this, SelectedEntry!);

    [RelayCommand]
    private void NewEntry() => AddRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ChangeMasterPassword() => ChangeMasterRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SetupOtp() => OtpSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Reveal()
    {
        StatusMessage = null;
        if (!_vault.HasOtp)
        {
            // design R5·7.4: 열람은 OTP 게이트를 거친다. 미등록이면 먼저 등록을 안내한다.
            StatusMessage = "비밀번호를 보려면 먼저 OTP를 등록하세요.";
            return;
        }
        RevealRequested?.Invoke(this, SelectedEntry!);
    }

    [RelayCommand]
    private void Lock()
    {
        _vault.Lock();
        Locked?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Backup() => BackupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>선택한 경로로 볼트를 백업한다(뷰가 대화상자에서 경로를 받아 호출). M6.</summary>
    public void PerformBackup(string path) => _vault.Backup(path);

    /// <summary>백업 파일로 복원하고 잠금 화면으로 돌아간다(백업의 마스터 비번으로 재로그인). M6.</summary>
    public void PerformRestore(string path)
    {
        _vault.Restore(path);                  // 세션 닫힘
        Locked?.Invoke(this, EventArgs.Empty); // 언락 화면으로 전환
    }
}
