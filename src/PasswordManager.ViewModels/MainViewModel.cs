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
    private VaultEntry? _selectedEntry;

    /// <summary>볼트가 잠겼을 때 발생. 셸이 구독해 언락 화면으로 돌아간다.</summary>
    public event EventHandler? Locked;

    /// <summary>새 항목 추가 요청. 셸이 편집 화면을 연다.</summary>
    public event EventHandler? AddRequested;

    /// <summary>선택 항목 편집 요청. 셸이 편집 화면을 연다.</summary>
    public event EventHandler<VaultEntry>? EditRequested;

    /// <summary>마스터 비밀번호 변경 요청. 셸이 변경 화면을 연다.</summary>
    public event EventHandler? ChangeMasterRequested;

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
    private void Lock()
    {
        _vault.Lock();
        Locked?.Invoke(this, EventArgs.Empty);
    }
}
