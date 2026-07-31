using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>게이트의 용도. 보기는 비번을 그 자리에서 노출하고, 편집은 확인만 하고 편집 화면으로 넘긴다.</summary>
public enum OtpGatePurpose
{
    /// <summary>비밀번호 열람(검증 성공 시 그 자리에서 노출).</summary>
    Reveal,
    /// <summary>편집 진입 전 확인(검증 성공 시 <see cref="OtpGateViewModel.Verified"/>만 발생).</summary>
    Edit,
    /// <summary>항목 잠금 해제만(검증 성공 시 <see cref="OtpGateViewModel.Verified"/>만 발생, 목록으로 복귀).
    /// 통과하면 그 항목의 보기·편집·삭제가 열린다.</summary>
    Verify,
}

/// <summary>
/// 개별 비밀번호 열람·편집 재확인 게이트 ViewModel (design 5.4·7.4, TD-004). 대상 항목을
/// 곧바로 열지 않고 OTP 6자리를 요구한 뒤, <see cref="VaultManager.VerifyOtp(string, DateTimeOffset)"/>로
/// 검증에 성공한 순간에만 진행한다. 보기는 <see cref="RevealedPassword"/>를 노출("지연 표시"),
/// 편집은 <see cref="Verified"/>를 발생시켜 셸이 편집 화면을 연다. 한 항목에서 한 번 통과하면 셸이
/// 세션 동안 그 항목을 기억해(<paramref name="preVerified"/>) 다른 동작은 코드 없이 바로 연다.
/// OTP는 암호학적 잠금이 아니라 어깨너머·자리비움을 막는 문지기다(한계는 TD-004).
/// </summary>
public sealed partial class OtpGateViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly VaultEntry _entry;
    private readonly Func<DateTimeOffset> _now;
    private readonly ClipboardCopier? _copier;

    public OtpGateViewModel(VaultManager vault, VaultEntry entry,
        Func<DateTimeOffset>? now = null, ClipboardCopier? copier = null,
        OtpGatePurpose purpose = OtpGatePurpose.Reveal, bool preVerified = false)
    {
        _vault = vault;
        _entry = entry;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _copier = copier;
        Purpose = purpose;
        RequiresCode = !preVerified;
        // 이미 이번 세션에 통과한 항목의 보기는 코드 없이 즉시 노출한다(그레이스, TD-004).
        if (preVerified && purpose == OtpGatePurpose.Reveal)
            RevealContents();
    }

    /// <summary>이 게이트의 용도(보기/편집).</summary>
    public OtpGatePurpose Purpose { get; }

    /// <summary>OTP 코드 입력이 필요한지. 그레이스로 이미 통과한 보기면 false(입력란·버튼 숨김).</summary>
    public bool RequiresCode { get; }

    /// <summary>화면 제목(용도에 따라 다름).</summary>
    public string Heading => Purpose switch
    {
        OtpGatePurpose.Edit => "편집 전 확인",
        OtpGatePurpose.Verify => "인증",
        _ => "비밀번호 보기",
    };

    /// <summary>기본 동작 버튼 문구.</summary>
    public string ActionLabel => Purpose switch
    {
        OtpGatePurpose.Edit => "확인",
        OtpGatePurpose.Verify => "인증",
        _ => "보기",
    };

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

    /// <summary>보기 용도에서 OTP 검증에 성공해 비밀번호를 노출했을 때 발생.</summary>
    public event EventHandler? Revealed;

    /// <summary>편집 용도에서 OTP 검증에 성공했을 때 발생. 셸이 편집 화면을 연다.</summary>
    public event EventHandler? Verified;

    /// <summary>열람을 취소하고 이전 화면으로 돌아갈 때 발생.</summary>
    public event EventHandler? Cancelled;

    private bool CanReveal() => !string.IsNullOrWhiteSpace(VerificationCode);

    [RelayCommand(CanExecute = nameof(CanReveal))]
    private void Reveal()
    {
        ErrorMessage = null;
        if (!_vault.VerifyOtp(VerificationCode, _now()))
        {
            ErrorMessage = "코드가 올바르지 않습니다. 디바이스의 6자리 코드를 다시 확인하세요.";
            return;
        }

        // 보기가 아닌 용도(편집·인증)는 비번을 노출하지 않고 검증 사실만 알린다.
        if (Purpose != OtpGatePurpose.Reveal)
        {
            Verified?.Invoke(this, EventArgs.Empty);
            return;
        }

        RevealContents();
        Revealed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>대상 항목의 평문 비밀번호와 이력을 노출한다(검증 통과 후에만 호출).</summary>
    private void RevealContents()
    {
        RevealedPassword = _entry.Password;
        RevealedHistory.Clear();
        foreach (var item in _entry.PasswordHistory)
            RevealedHistory.Add(item);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>주어진 비밀번호(현재 또는 이력 항목)를 클립보드에 복사하고 자동 삭제를 예약한다(design 5.5).</summary>
    [RelayCommand]
    private void Copy(string? password) => _copier?.CopyWithAutoClear(password);
}
