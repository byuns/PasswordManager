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
        if (existing is not null)
        {
            Title = existing.Title;
            Url = existing.Url;
            Login = existing.Login;
            Password = existing.Password;
            Notes = existing.Notes;
        }
    }

    /// <summary>새 항목 작성 여부(false면 기존 항목 편집).</summary>
    public bool IsNew => _original is null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _login = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    /// <summary>저장 완료 시 발생. 셸이 구독해 메인으로 돌아간다.</summary>
    public event EventHandler? Saved;

    /// <summary>편집 취소 시 발생.</summary>
    public event EventHandler? Cancelled;

    private bool CanSave() => !string.IsNullOrWhiteSpace(Title);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_original is null)
        {
            _vault.Add(new VaultEntry
            {
                Title = Title,
                Url = Url,
                Login = Login,
                Password = Password,
                Notes = Notes,
            });
        }
        else
        {
            _original.Title = Title;
            _original.Url = Url;
            _original.Login = Login;
            _original.Password = Password;
            _original.Notes = Notes;
            _vault.Update(_original);
        }
        Saved?.Invoke(this, EventArgs.Empty);
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
