using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>같은 사이트(제목)로 묶인 계정들의 그룹. 목록은 사이트별로 묶어 보여준다(TD-003 그룹 표시).</summary>
public sealed class SiteGroup
{
    public SiteGroup(string siteName) => SiteName = siteName;

    /// <summary>그룹 헤더에 표시할 사이트명.</summary>
    public string SiteName { get; }

    /// <summary>이 사이트에 속한 계정들(입력 순서 보존).</summary>
    public ObservableCollection<VaultEntry> Accounts { get; } = new();

    /// <summary>계정이 둘 이상인지(뷰가 헤더 강조 등에 활용).</summary>
    public bool HasMultipleAccounts => Accounts.Count > 1;
}

/// <summary>
/// 언락 후 메인 화면 ViewModel. 항목 목록을 사이트별로 묶어 보여주고 제목·로그인으로 검색하며,
/// 선택 항목의 편집/삭제와 볼트 잠금을 처리한다. 추가/편집은 이벤트로 셸에 위임한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly ClipboardCopier? _copier;

    public MainViewModel(VaultManager vault, ClipboardCopier? copier = null)
    {
        _vault = vault;
        _copier = copier;
        Refresh();
    }

    /// <summary>현재 화면에 보이는(검색 필터가 적용된) 항목들(평면).</summary>
    public ObservableCollection<VaultEntry> Entries { get; } = new();

    /// <summary>같은 사이트끼리 묶은 그룹 목록(검색 필터 적용). 뷰는 이걸 렌더링한다.</summary>
    public ObservableCollection<SiteGroup> Groups { get; } = new();

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

    /// <summary>볼트가 잠겼을 때 발생. 셸이 구독해 언락 화면으로 돌아간다.</summary>
    public event EventHandler? Locked;

    /// <summary>새 항목 추가 요청. 셸이 편집 화면을 연다.</summary>
    public event EventHandler? AddRequested;

    /// <summary>선택 항목 편집 요청(OTP 등록 시에만 발생). 셸이 OTP 게이트→편집 화면을 연다.</summary>
    public event EventHandler<VaultEntry>? EditRequested;

    /// <summary>선택 항목 비밀번호 열람 요청. 셸이 OTP 게이트를 연다(design 7.4).</summary>
    public event EventHandler<VaultEntry>? RevealRequested;

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

        var filtered = source.ToList();

        Entries.Clear();
        foreach (var entry in filtered)
            Entries.Add(entry);

        // 사이트명(대소문자 무시) 첫 등장 순서를 보존하며 그룹으로 묶는다.
        Groups.Clear();
        var index = new Dictionary<string, SiteGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in filtered)
        {
            if (!index.TryGetValue(entry.Title, out var group))
            {
                group = new SiteGroup(entry.Title);
                index[entry.Title] = group;
                Groups.Add(group);
            }
            group.Accounts.Add(entry);
        }
    }

    /// <summary>
    /// 카드 액션 대상. 카드 버튼은 자기 항목을 인자로 넘기고(<paramref name="entry"/>),
    /// 인자가 없으면(예: 하단 공용 버튼) 선택 항목으로 폴백한다. design-ux §4.
    /// </summary>
    private bool CanActOn(VaultEntry? entry) => (entry ?? SelectedEntry) is not null;

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private void Delete(VaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target is null) return;
        _vault.Remove(target.Id);
        if (ReferenceEquals(target, SelectedEntry)) SelectedEntry = null;
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private void Edit(VaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target is null) return;
        StatusMessage = null;
        if (!_vault.HasOtp)
        {
            // 편집은 기존 비밀번호를 노출하므로 열람과 동일하게 OTP 게이트를 거친다(TD-004).
            // 미등록이면 먼저 등록을 안내한다.
            StatusMessage = "비밀번호를 편집하려면 먼저 OTP를 등록하세요.";
            return;
        }
        EditRequested?.Invoke(this, target);
    }

    [RelayCommand]
    private void NewEntry() => AddRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>아이디를 클립보드에 복사한다(비밀 아님 → OTP 게이트 없이 바로). design 7.4.</summary>
    [RelayCommand]
    private void CopyLogin(string? login) => _copier?.Copy(login);

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private void Reveal(VaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target is null) return;
        StatusMessage = null;
        if (!_vault.HasOtp)
        {
            // design R5·7.4: 열람은 OTP 게이트를 거친다. 미등록이면 먼저 등록을 안내한다.
            StatusMessage = "비밀번호를 보려면 먼저 OTP를 등록하세요.";
            return;
        }
        RevealRequested?.Invoke(this, target);
    }

    [RelayCommand]
    private void Lock()
    {
        _vault.Lock();
        Locked?.Invoke(this, EventArgs.Empty);
    }
}
