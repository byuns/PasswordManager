using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Vault;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.ViewModels;

/// <summary>휴지통 목록 한 줄. 항목과 함께 "며칠 뒤 완전히 지워지는지"를 들고 있다(TD-041).</summary>
public sealed record TrashItem(VaultEntry Entry, int DaysLeft);

/// <summary>
/// 휴지통 화면 ViewModel (TD-041). 삭제한 계정을 되살리거나 영구 삭제한다.
/// 복원·영구삭제는 비밀번호를 노출하지 않으므로 OTP 게이트를 거치지 않는다(TD-032 범위 밖).
/// 되돌릴 수 없는 동작(영구삭제·비우기)만 확인 다이얼로그를 받는다.
/// </summary>
public sealed partial class TrashViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly IDialogService? _dialog;
    private readonly Func<DateTimeOffset> _now;

    public TrashViewModel(VaultManager vault, IDialogService? dialog = null, Func<DateTimeOffset>? now = null)
    {
        _vault = vault;
        _dialog = dialog;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Refresh();
    }

    /// <summary>휴지통에 든 항목들(최근 삭제 순).</summary>
    public ObservableCollection<TrashItem> Items { get; } = new();

    /// <summary>휴지통이 비었는가(뷰가 빈 상태 안내를 띄우는 조건).</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>보관 기간(일). 뷰의 안내 문구에 쓴다.</summary>
    public int RetentionDays => VaultManager.TrashRetentionDays;

    /// <summary>화면을 닫아 달라는 요청. 셸이 설정 화면으로 되돌린다.</summary>
    public event EventHandler? Closed;

    /// <summary>볼트에서 휴지통 목록을 다시 읽어 남은 일수를 계산한다.</summary>
    public void Refresh()
    {
        var now = _now();

        Items.Clear();
        foreach (var entry in _vault.DeletedEntries)
        {
            // 삭제 시점 + 보관기간이 만료일. 이미 지났으면 0으로 바닥을 깐다(언락 때 정리되므로 과도기일 뿐).
            var elapsed = (now - entry.DeletedAt!.Value).TotalDays;
            var daysLeft = (int)Math.Ceiling(VaultManager.TrashRetentionDays - elapsed);
            Items.Add(new TrashItem(entry, Math.Max(0, daysLeft)));
        }

        OnPropertyChanged(nameof(IsEmpty));
        EmptyTrashCommand.NotifyCanExecuteChanged();
    }

    /// <summary>항목을 계정 목록으로 되살린다. 되돌릴 수 있는 동작이라 확인을 받지 않는다.</summary>
    [RelayCommand]
    private void Restore(TrashItem? item)
    {
        if (item is null) return;
        _vault.RestoreEntry(item.Entry.Id);
        Refresh();
        _dialog?.Notify("복원됨", $"‘{item.Entry.Title}’ 계정을 되살렸습니다.");
    }

    /// <summary>항목을 영구 삭제한다(되돌릴 수 없음 → 확인 필수).</summary>
    [RelayCommand]
    private async Task PurgeAsync(TrashItem? item)
    {
        if (item is null) return;
        if (_dialog is not null && !await _dialog.ConfirmAsync(
                "영구 삭제",
                $"‘{item.Entry.Title}’ 계정을 완전히 지울까요?\n이 동작은 되돌릴 수 없습니다.",
                "영구 삭제", "취소"))
            return;

        _vault.PurgeEntry(item.Entry.Id);
        Refresh();
        _dialog?.Notify("삭제됨", "계정을 완전히 지웠습니다.");
    }

    /// <summary>휴지통을 통째로 비운다(되돌릴 수 없음 → 확인 필수).</summary>
    [RelayCommand(CanExecute = nameof(CanEmptyTrash))]
    private async Task EmptyTrashAsync()
    {
        var count = Items.Count;
        if (_dialog is not null && !await _dialog.ConfirmAsync(
                "휴지통 비우기",
                $"휴지통의 계정 {count}개를 완전히 지울까요?\n이 동작은 되돌릴 수 없습니다.",
                "비우기", "취소"))
            return;

        _vault.EmptyTrash();
        Refresh();
        _dialog?.Notify("비웠습니다", $"계정 {count}개를 완전히 지웠습니다.");
    }

    private bool CanEmptyTrash() => !IsEmpty;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}
