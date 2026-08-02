using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels;

/// <summary>같은 사이트(제목)로 묶인 계정들의 그룹. 목록은 사이트별로 묶어 보여준다(TD-003 그룹 표시).</summary>
public sealed class SiteGroup
{
    public SiteGroup(string siteName, bool isFavorites = false)
    {
        SiteName = siteName;
        IsFavorites = isFavorites;
    }

    /// <summary>그룹 헤더에 표시할 사이트명.</summary>
    public string SiteName { get; }

    /// <summary>즐겨찾기 전용 그룹인가(TD-040). 사이트가 아니라 핀한 계정들을 모아 맨 위에 놓는다.</summary>
    public bool IsFavorites { get; }

    /// <summary>이 사이트에 속한 계정들.</summary>
    public ObservableCollection<VaultEntry> Accounts { get; } = new();

    /// <summary>계정이 둘 이상인지(뷰가 헤더 강조 등에 활용).</summary>
    public bool HasMultipleAccounts => Accounts.Count > 1;
}

/// <summary>정렬 드롭다운 한 줄(정렬 기준 + 표시 이름).</summary>
public sealed record SortOption(EntrySortOrder Order, string Label);

/// <summary>
/// 언락 후 메인 화면 ViewModel. 항목 목록을 사이트별로 묶어 보여주고 제목·로그인으로 검색하며,
/// 선택 항목의 편집/삭제와 볼트 잠금을 처리한다. 추가/편집은 이벤트로 셸에 위임한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly ClipboardCopier? _copier;
    private readonly ISet<string> _otpVerified;
    private readonly IDialogService? _dialog;

    public MainViewModel(VaultManager vault, ClipboardCopier? copier = null, ISet<string>? otpVerified = null,
        IDialogService? dialog = null)
    {
        _vault = vault;
        _copier = copier;
        _otpVerified = otpVerified ?? new HashSet<string>();
        _dialog = dialog;
        _sortOrder = vault.SortOrder; // 볼트에 저장된 정렬 기준을 이어받는다(TD-040)
        Refresh();
    }

    /// <summary>정렬 드롭다운에 채울 선택지(모든 정렬 기준 + 표시 이름).</summary>
    public IReadOnlyList<SortOption> SortOptions { get; } = new[]
    {
        new SortOption(EntrySortOrder.Name, "이름순"),
        new SortOption(EntrySortOrder.RecentlyChanged, "최근 변경순"),
        new SortOption(EntrySortOrder.RecentlyUsed, "최근 사용순"),
    };

    /// <summary>이번 세션에 OTP 게이트를 통과한 항목 ID들(셸과 공유). 메모 hover 미리보기를
    /// 이 집합에 든 항목에만 노출한다(열람·편집과 동일한 게이트, design 7.4).</summary>
    public IEnumerable<string> OtpVerifiedIds => _otpVerified;

    /// <summary>볼트에 OTP가 등록돼 있는지. 미등록이면 행 버튼을 'OTP 등록' 유도로 바꾼다(TD-032 후속).</summary>
    public bool HasOtp => _vault.HasOtp;

    /// <summary>OTP 미등록 상태(=등록 유도 버튼 노출 조건). <see cref="HasOtp"/>의 반대.</summary>
    public bool RequiresOtpSetup => !_vault.HasOtp;

    /// <summary>현재 화면에 보이는(검색 필터가 적용된) 항목들(평면).</summary>
    public ObservableCollection<VaultEntry> Entries { get; } = new();

    /// <summary>같은 사이트끼리 묶은 그룹 목록(검색 필터 적용). 뷰는 이걸 렌더링한다.</summary>
    public ObservableCollection<SiteGroup> Groups { get; } = new();

    /// <summary>전체 항목에 존재하는 고유 태그들(가나다/알파벳 순). 태그 pane이 이걸 렌더링한다.</summary>
    public ObservableCollection<string> AvailableTags { get; } = new();

    /// <summary>현재 켜져 있는 태그 필터. 여러 개면 OR(합집합)로 묶고 검색어와는 AND(TD-029).</summary>
    public ObservableCollection<string> SelectedTags { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>키보드(↑/↓)·마우스로 고른 현재 행. 뷰는 이 값으로 선택 강조를 그리고,
    /// 인자 없는 단축키 커맨드(Enter·Ctrl+B 등)의 대상이 된다.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevealCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedLoginCommand))]
    private VaultEntry? _selectedEntry;

    /// <summary>
    /// 선택된 행이 즐겨찾기 그룹 쪽인가(TD-040). 핀한 계정은 즐겨찾기 그룹과 사이트 그룹에 **같은
    /// 인스턴스**로 두 번 나오므로, 항목만 비교하면 두 행이 동시에 강조된다. 이 플래그로 한쪽만 켠다.
    /// </summary>
    [ObservableProperty]
    private bool _selectionInFavorites;

    /// <summary>행을 선택한다. 뷰가 클릭한 행이 어느 그룹에 속했는지 함께 알려준다.</summary>
    public void Select(VaultEntry? entry, bool inFavorites)
    {
        SelectedEntry = entry;
        SelectionInFavorites = entry is not null && inFavorites;
    }

    /// <summary>사용자에게 보여줄 일시 안내(예: OTP 미등록 시 열람 안내). design 5.4.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>계정 목록 정렬 기준(TD-040). 바꾸면 볼트에 저장돼 다음 실행에도 유지된다.</summary>
    [ObservableProperty]
    private EntrySortOrder _sortOrder;

    partial void OnSortOrderChanged(EntrySortOrder value)
    {
        _vault.SetSortOrder(value);
        Refresh();
    }

    /// <summary>볼트가 잠겼을 때 발생. 셸이 구독해 언락 화면으로 돌아간다.</summary>
    public event EventHandler? Locked;

    /// <summary>새 항목 추가 요청. 셸이 편집 화면을 연다.</summary>
    public event EventHandler? AddRequested;

    /// <summary>선택 항목 편집 요청(OTP 등록 시에만 발생). 셸이 OTP 게이트→편집 화면을 연다.</summary>
    public event EventHandler<VaultEntry>? EditRequested;

    /// <summary>선택 항목 비밀번호 열람 요청. 셸이 OTP 게이트를 연다(design 7.4).</summary>
    public event EventHandler<VaultEntry>? RevealRequested;

    /// <summary>항목 인증(잠금 해제) 요청. 셸이 OTP 게이트를 열고, 통과하면 그 항목의
    /// 보기·편집·삭제 버튼이 열린다(행별 단일 '인증' 버튼 → 3버튼 전환).</summary>
    public event EventHandler<VaultEntry>? VerifyRequested;

    /// <summary>OTP 미등록 상태에서 인증을 시도했을 때 발생. 셸이 OTP 등록 화면으로 안내한다(TD-032 후속).</summary>
    public event EventHandler? OtpSetupRequested;

    partial void OnSearchQueryChanged(string value) => Refresh();

    /// <summary>볼트에서 항목을 다시 읽어 검색·태그 필터를 적용해 목록을 갱신한다.</summary>
    public void Refresh()
    {
        RebuildAvailableTags(); // 태그 목록·선택 상태를 현재 볼트 기준으로 먼저 정리

        var query = SearchQuery?.Trim() ?? string.Empty;
        IEnumerable<VaultEntry> source = _vault.Entries;
        if (query.Length > 0)
            source = source.Where(e =>
                e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Login.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));

        // 선택 태그끼리는 OR(하나라도 달렸으면 통과), 검색어와는 AND로 결합(TD-029).
        if (SelectedTags.Count > 0)
            source = source.Where(e => e.Tags.Any(SelectedTags.Contains));

        var filtered = Sorted(source).ToList();

        Entries.Clear();
        foreach (var entry in filtered)
            Entries.Add(entry);

        Groups.Clear();

        // 즐겨찾기는 사이트와 무관하게 한 그룹으로 모아 맨 위에 둔다(TD-040).
        // "바로가기"라 원래 사이트 그룹에서 빼지 않는다 — 아래에도 그대로 나온다.
        // 검색·태그 필터를 통과한 것만 담기므로, 필터에 안 걸리면 이 그룹도 사라진다.
        var pinned = filtered.Where(e => e.IsPinned).ToList();
        if (pinned.Count > 0)
        {
            var favorites = new SiteGroup("즐겨찾기", isFavorites: true);
            foreach (var entry in pinned)
                favorites.Accounts.Add(entry);
            Groups.Add(favorites);
        }

        // 이어서 사이트명(대소문자 무시)으로 묶는다. 정렬된 순서를 그대로 이어받는다.
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

        // 필터·삭제로 화면에서 사라진 항목이 선택된 채 남으면 단축키가 안 보이는 행을 건드리게 된다.
        // 편집 저장은 항목을 새 인스턴스로 교체하므로(TD-021) 참조가 아니라 id로 뒤쫓는다 —
        // 그러지 않으면 편집하고 돌아왔을 때 선택이 조용히 풀려 그 행으로 되돌아갈 수 없다(TD-049).
        if (SelectedEntry is not null)
        {
            var still = filtered.FirstOrDefault(e => e.Id == SelectedEntry.Id);
            if (still is null)
                Select(null, inFavorites: false);
            else
            {
                if (!ReferenceEquals(still, SelectedEntry)) SelectedEntry = still;
                if (SelectionInFavorites && !still.IsPinned)
                    SelectionInFavorites = false; // 핀이 풀리면 즐겨찾기 그룹 자체가 없어진다
            }
        }

        // OTP 등록 상태가 바뀌었을 수 있으니(예: 등록 후 복귀) 행 버튼 전환용 플래그를 갱신 통지한다.
        OnPropertyChanged(nameof(HasOtp));
        OnPropertyChanged(nameof(RequiresOtpSetup));
    }

    /// <summary>
    /// 현재 정렬 기준으로 항목을 줄 세운다(TD-040). 그룹 순서는 이 순서를 이어받으므로,
    /// 같은 사이트의 계정끼리도 같은 기준으로 정렬된다. 날짜가 없는 항목(쓴 적 없음)은 뒤로 민다.
    /// </summary>
    private IEnumerable<VaultEntry> Sorted(IEnumerable<VaultEntry> source) => SortOrder switch
    {
        EntrySortOrder.RecentlyChanged => source
            .OrderByDescending(e => e.LastChangedAt)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
        EntrySortOrder.RecentlyUsed => source
            .OrderByDescending(e => e.LastUsedAt.HasValue) // 한 번도 안 쓴 항목을 맨 뒤로
            .ThenByDescending(e => e.LastUsedAt)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
        _ => source
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Login, StringComparer.CurrentCultureIgnoreCase),
    };

    /// <summary>전체 항목의 고유 태그로 <see cref="AvailableTags"/>를 다시 만들고,
    /// 더 이상 존재하지 않는 태그는 <see cref="SelectedTags"/>에서 걷어낸다.</summary>
    private void RebuildAvailableTags()
    {
        // 선택된 태그를 가장 좌측으로 모으고, 각 그룹(선택/미선택) 안에서는 사전순.
        // 선택을 해제하면 다시 사전순 자리로 돌아온다(대칭).
        var tags = _vault.Entries
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => SelectedTags.Contains(t))
            .ThenBy(t => t, StringComparer.CurrentCulture)
            .ToList();

        AvailableTags.Clear();
        foreach (var tag in tags)
            AvailableTags.Add(tag);

        // 사라진 태그(예: 마지막 보유 항목 삭제)는 필터에서 제거한다.
        for (var i = SelectedTags.Count - 1; i >= 0; i--)
            if (!AvailableTags.Contains(SelectedTags[i]))
                SelectedTags.RemoveAt(i);
    }

    /// <summary>태그 필터를 켜거나 끈다(이미 켜져 있으면 해제). design 3-pane 태그 필터.</summary>
    [RelayCommand]
    private void ToggleTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (!SelectedTags.Remove(tag))
            SelectedTags.Add(tag);
        Refresh();
    }

    /// <summary>모든 태그 필터를 해제한다.</summary>
    [RelayCommand]
    private void ClearTags()
    {
        if (SelectedTags.Count == 0) return;
        SelectedTags.Clear();
        Refresh();
    }

    /// <summary>검색어와 태그 필터를 한 번에 되돌려 전체 목록으로 복귀한다(Esc).</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SelectedTags.Clear();
        // SearchQuery 세터가 Refresh를 부르지만, 이미 비어 있으면 안 불리므로 아래서 한 번 더 부른다.
        SearchQuery = string.Empty;
        Refresh();
    }

    /// <summary>목록에서 선택을 한 칸 이동한다(↑/↓). 끝에서는 순환하지 않고 머문다.</summary>
    /// <param name="step">+1이면 다음, -1이면 이전.</param>
    private void MoveSelection(int step)
    {
        if (Entries.Count == 0) return;

        var current = SelectedEntry is null ? -1 : Entries.IndexOf(SelectedEntry);
        if (current < 0)
        {
            // 선택이 없을 땐 진행 방향의 가장 가까운 끝에서 시작한다(↓=첫 항목, ↑=마지막 항목).
            // 키보드 이동은 중복 없는 평면 목록 기준이라 강조도 원래 자리(사이트 그룹)에 준다.
            Select(step > 0 ? Entries[0] : Entries[^1], inFavorites: false);
            return;
        }

        var next = current + step;
        if (next < 0 || next >= Entries.Count) return; // 양끝에서 정지
        Select(Entries[next], inFavorites: false);
    }

    /// <summary>다음 항목 선택(↓). 선택이 없으면 첫 항목.</summary>
    [RelayCommand]
    private void SelectNext() => MoveSelection(1);

    /// <summary>이전 항목 선택(↑). 선택이 없으면 마지막 항목.</summary>
    [RelayCommand]
    private void SelectPrevious() => MoveSelection(-1);

    /// <summary>선택 항목의 아이디를 복사한다(Ctrl+B). 비밀이 아니라 OTP 게이트 없이 바로.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopySelectedLogin() => _copier?.Copy(SelectedEntry?.Login);

    /// <summary>인자 없이 선택 항목만 대상으로 하는 단축키 커맨드의 실행 조건.</summary>
    private bool HasSelection() => SelectedEntry is not null;

    /// <summary>
    /// 카드 액션 대상. 카드 버튼은 자기 항목을 인자로 넘기고(<paramref name="entry"/>),
    /// 인자가 없으면(예: 하단 공용 버튼) 선택 항목으로 폴백한다. design-ux §4.
    /// </summary>
    private bool CanActOn(VaultEntry? entry) => (entry ?? SelectedEntry) is not null;

    [RelayCommand(CanExecute = nameof(CanActOn))]
    private async Task DeleteAsync(VaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target is null) return;
        // 실수 방지: 다이얼로그가 있으면 삭제 전 확인을 받는다(취소 시 아무것도 안 함).
        if (_dialog is not null && !await _dialog.ConfirmAsync(
                "삭제 확인",
                $"‘{target.Title}’ 계정을 삭제할까요?\n휴지통으로 옮겨지며 {VaultManager.TrashRetentionDays}일 뒤 완전히 지워집니다.",
                "삭제", "취소"))
            return;
        _vault.Remove(target.Id);
        Refresh(); // 사라진 항목이 선택돼 있었다면 여기서 함께 해제된다

        _dialog?.Notify("삭제됨", "휴지통으로 옮겼습니다. 설정 > 백업·데이터에서 되살릴 수 있습니다.");
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

    /// <summary>행별 '인증' 버튼. OTP 게이트를 열어 이 항목의 보기·편집·삭제를 연다.
    /// 미등록이면 먼저 등록을 안내한다(design R5·7.4).</summary>
    [RelayCommand(CanExecute = nameof(CanActOn))]
    private void Verify(VaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target is null) return;
        StatusMessage = null;
        if (!_vault.HasOtp)
        {
            // 미등록이면 막지 말고 등록 화면으로 안내한다(등록해야 보기·편집·삭제가 열림).
            OtpSetupRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        VerifyRequested?.Invoke(this, target);
    }

    /// <summary>즐겨찾기 고정을 켜고 끈다(TD-040). 비밀 노출이 아니라 OTP 게이트를 거치지 않는다.</summary>
    [RelayCommand]
    private void TogglePin(VaultEntry? entry)
    {
        var target = entry ?? SelectedEntry;
        if (target is null) return;
        _vault.SetPinned(target.Id, !target.IsPinned);
        Refresh();
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
