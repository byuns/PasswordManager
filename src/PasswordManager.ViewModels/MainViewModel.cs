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
    private readonly ISet<string> _otpVerified;
    private readonly IDialogService? _dialog;

    public MainViewModel(VaultManager vault, ClipboardCopier? copier = null, ISet<string>? otpVerified = null,
        IDialogService? dialog = null)
    {
        _vault = vault;
        _copier = copier;
        _otpVerified = otpVerified ?? new HashSet<string>();
        _dialog = dialog;
        Refresh();
    }

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
                e.Login.Contains(query, StringComparison.OrdinalIgnoreCase));

        // 선택 태그끼리는 OR(하나라도 달렸으면 통과), 검색어와는 AND로 결합(TD-029).
        if (SelectedTags.Count > 0)
            source = source.Where(e => e.Tags.Any(SelectedTags.Contains));

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

        // OTP 등록 상태가 바뀌었을 수 있으니(예: 등록 후 복귀) 행 버튼 전환용 플래그를 갱신 통지한다.
        OnPropertyChanged(nameof(HasOtp));
        OnPropertyChanged(nameof(RequiresOtpSetup));
    }

    /// <summary>전체 항목의 고유 태그로 <see cref="AvailableTags"/>를 다시 만들고,
    /// 더 이상 존재하지 않는 태그는 <see cref="SelectedTags"/>에서 걷어낸다.</summary>
    private void RebuildAvailableTags()
    {
        var tags = _vault.Entries
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.CurrentCulture)
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
                "삭제 확인", $"‘{target.Title}’ 계정을 삭제할까요?\n삭제하면 되돌릴 수 없습니다.", "삭제", "취소"))
            return;
        _vault.Remove(target.Id);
        if (ReferenceEquals(target, SelectedEntry)) SelectedEntry = null;
        Refresh();
        _dialog?.Notify("삭제됨", "계정을 삭제했습니다.");
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
