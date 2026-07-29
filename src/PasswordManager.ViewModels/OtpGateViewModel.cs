using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 개별 비밀번호 열람 재확인 게이트 ViewModel (design 5.4·7.4, TD-004). 대상 항목의 비밀번호를
/// 곧바로 보여주지 않고 OTP 6자리를 요구한 뒤, <see cref="VaultManager.VerifyOtp(string, DateTimeOffset)"/>로
/// 검증에 성공한 순간에만 <see cref="RevealedPassword"/>를 노출한다("지연 표시"). OTP는 암호학적
/// 잠금이 아니라 어깨너머·자리비움을 막는 문지기다(한계는 TD-004).
/// </summary>
public sealed partial class OtpGateViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly VaultEntry _entry;
    private readonly Func<DateTimeOffset> _now;
    private readonly ClipboardCopier? _copier;

    public OtpGateViewModel(VaultManager vault, VaultEntry entry,
        Func<DateTimeOffset>? now = null, ClipboardCopier? copier = null)
    {
        _vault = vault;
        _entry = entry;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _copier = copier;
    }

    /// <summary>어떤 항목을 여는지 화면에 보여주기 위한 제목.</summary>
    public string Title => _entry.Title;

    /// <summary>비밀번호를 마지막으로 바꾼 시각(비밀 아님, 게이트 통과 전에도 표시 가능). M4·TD-021.</summary>
    public DateTimeOffset LastChangedAt => _entry.LastChangedAt;

    /// <summary>
    /// OTP 검증 성공 후에만 채워지는 이전 비밀번호 이력(최신이 앞). 옛 비번도 secret이므로 게이트를
    /// 통과해야만 노출한다(TD-004·TD-021). 검증 전에는 비어 있다.
    /// </summary>
    public ObservableCollection<PasswordHistoryItem> RevealedHistory { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealCommand))]
    private string _verificationCode = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>검증 성공 후에만 채워지는 평문 비밀번호. 그 전에는 null(마스킹).</summary>
    [ObservableProperty]
    private string? _revealedPassword;

    /// <summary>OTP 검증에 성공해 비밀번호를 노출했을 때 발생.</summary>
    public event EventHandler? Revealed;

    /// <summary>열람을 취소하고 이전 화면으로 돌아갈 때 발생.</summary>
    public event EventHandler? Cancelled;

    private bool CanReveal() => !string.IsNullOrWhiteSpace(VerificationCode);

    [RelayCommand(CanExecute = nameof(CanReveal))]
    private void Reveal()
    {
        ErrorMessage = null;
        if (!_vault.VerifyOtp(VerificationCode, _now()))
        {
            ErrorMessage = "코드가 올바르지 않습니다. 폰의 6자리 코드를 다시 확인하세요.";
            return;
        }

        RevealedPassword = _entry.Password;
        RevealedHistory.Clear();
        foreach (var item in _entry.PasswordHistory)
            RevealedHistory.Add(item);
        Revealed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>주어진 비밀번호(현재 또는 이력 항목)를 클립보드에 복사하고 자동 삭제를 예약한다(design 5.5).</summary>
    [RelayCommand]
    private void Copy(string? password) => _copier?.CopyWithAutoClear(password);
}
