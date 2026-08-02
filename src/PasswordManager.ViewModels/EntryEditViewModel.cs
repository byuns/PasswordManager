using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.ViewModels;

/// <summary>
/// 항목 추가/편집 폼 ViewModel. 새 항목은 <see cref="VaultManager.Add"/>, 편집은
/// 원본 <see cref="VaultEntry"/>에 폼 필드를 반영해 <see cref="VaultManager.Update"/>한다.
/// 폼에 없는 필드(생성시각·이력·TOTP 비밀)는 원본을 그대로 재사용해 보존하고,
/// 취소하면 원본을 건드리지 않는다.
/// </summary>
public sealed partial class EntryEditViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly VaultEntry? _original; // null이면 새 항목

    public EntryEditViewModel(VaultManager vault, VaultEntry? existing = null)
    {
        _vault = vault;
        _original = existing;
        KnownSites = vault.Entries
            .Select(e => e.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (existing is not null)
        {
            Title = existing.Title;
            Url = existing.Url;
            Login = existing.Login;
            Password = existing.Password;
            Notes = existing.Notes;
            foreach (var tag in existing.Tags)
                Tags.Add(tag);
        }
    }

    /// <summary>새 항목 작성 여부(false면 기존 항목 편집).</summary>
    public bool IsNew => _original is null;

    /// <summary>
    /// 볼트에 이미 있는 사이트명들(중복 제거·이름순, 휴지통 제외). 사이트명 칸의 드롭다운 후보로,
    /// 고르면 <see cref="ApplySite"/>가 URL·태그를 함께 채운다(TD-046). 폼을 여는 시점의 스냅샷이다.
    /// </summary>
    public IReadOnlyList<string> KnownSites { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    [ObservableProperty] private string _url = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _login = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _password = string.Empty;

    [ObservableProperty] private string _notes = string.Empty;

    /// <summary>확정된 태그 칩들(입력창에서 Space/Enter로 추가). 인스타/블로그식 태그 입력.</summary>
    public ObservableCollection<string> Tags { get; } = new();

    /// <summary>태그 입력창의 현재 텍스트. Space/Enter 시 <see cref="AddTag"/>가 칩으로 옮긴다.</summary>
    [ObservableProperty] private string _tagInput = string.Empty;

    /// <summary>저장 완료 시 발생. 셸이 구독해 메인으로 돌아간다.</summary>
    public event EventHandler? Saved;

    /// <summary>편집 취소 시 발생.</summary>
    public event EventHandler? Cancelled;

    /// <summary>직전 저장이 비밀번호를 새로 추가하거나 바꿨는지(슬랙 "비밀번호 변경" 알림 판정용, design 7.8).</summary>
    public bool LastSaveChangedPassword { get; private set; }

    // 사이트명·아이디·비밀번호는 필수. 셋 다 채워야 저장할 수 있다.
    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(Title) &&
        !string.IsNullOrWhiteSpace(Login) &&
        !string.IsNullOrWhiteSpace(Password);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // 입력창에 아직 확정 안 한 태그가 남아 있으면 저장 시 함께 반영한다.
        if (!string.IsNullOrWhiteSpace(TagInput)) AddTag();

        // 신규 항목이거나 기존 비번과 다르면 비밀번호 추가·변경으로 본다(메타데이터만 바뀐 저장은 제외).
        LastSaveChangedPassword = _original is null || Password != _original.Password;

        if (_original is null)
        {
            _vault.Add(new VaultEntry
            {
                Title = Title,
                Url = Url,
                Login = Login,
                Password = Password,
                Notes = Notes,
                Tags = Tags.ToList(),
            });
        }
        else
        {
            // 원본을 직접 수정하지 않고 새 항목을 만들어 넘긴다(TD-021). 이렇게 해야
            // Update가 이전 비번을 비교해 이력을 적재할 수 있고, 저장 전 원본도 온전히 남는다.
            // 생성시각·이력은 Update가 기존 항목에서 이어받으므로 여기서 넘기지 않는다.
            _vault.Update(new VaultEntry
            {
                Id = _original.Id,
                Title = Title,
                Url = Url,
                Login = Login,
                Password = Password,
                Notes = Notes,
                Tags = Tags.ToList(),
                TotpSecret = _original.TotpSecret,
            });
        }
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 드롭다운에서 기존 사이트명을 고르면 그 사이트의 URL·태그를 함께 채운다(TD-046).
    /// 같은 사이트명 항목이 여럿이면 가장 최근에 수정된 항목을 기준으로 삼고,
    /// 사용자가 이미 친 값은 지우지 않는다 — URL은 비어 있을 때만 채우고 태그는 없는 것만 더한다.
    /// 목록에 없는 이름(직접 입력)은 아무것도 하지 않는다.
    /// </summary>
    [RelayCommand]
    private void ApplySite(string? site)
    {
        if (string.IsNullOrWhiteSpace(site)) return;

        var source = _vault.Entries
            .Where(e => string.Equals(e.Title, site, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.UpdatedAt)
            .FirstOrDefault();
        if (source is null) return;

        Title = source.Title;
        if (string.IsNullOrWhiteSpace(Url)) Url = source.Url;
        foreach (var tag in source.Tags)
            if (!Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                Tags.Add(tag);
    }

    /// <summary>입력창의 현재 텍스트를 태그 칩으로 옮긴다. '#'·공백·쉼표로 나눠 여러 개를 한 번에 처리하고,
    /// 트림·중복 제거(대소문자 무시) 후 추가한 뒤 입력창을 비운다.</summary>
    [RelayCommand]
    private void AddTag()
    {
        var tokens = TagInput.Split(
            new[] { ' ', ',', '#', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var tag in tokens)
            if (!Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                Tags.Add(tag);
        TagInput = string.Empty;
    }

    /// <summary>태그 칩을 제거한다(칩의 ✕ 버튼).</summary>
    [RelayCommand]
    private void RemoveTag(string? tag)
    {
        if (tag is null) return;
        var existing = Tags.FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Tags.Remove(existing);
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    // ── 비밀번호 생성기 (design 7.1) ──
    [ObservableProperty] private int _generatorLength = 16;
    [ObservableProperty] private bool _genUppercase = true;
    [ObservableProperty] private bool _genLowercase = true;
    [ObservableProperty] private bool _genDigits = true;
    [ObservableProperty] private bool _genSymbols = true;
    [ObservableProperty] private bool _genExcludeAmbiguous = true;

    [RelayCommand]
    private void GeneratePassword() =>
        Password = PasswordGenerator.Generate(new PasswordOptions(
            Length: GeneratorLength,
            IncludeUppercase: GenUppercase,
            IncludeLowercase: GenLowercase,
            IncludeDigits: GenDigits,
            IncludeSymbols: GenSymbols,
            ExcludeAmbiguous: GenExcludeAmbiguous));
}
