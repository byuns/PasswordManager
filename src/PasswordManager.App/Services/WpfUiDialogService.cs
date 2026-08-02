using System;
using System.Threading.Tasks;
using PasswordManager.ViewModels.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PasswordManager.App.Services;

/// <summary>
/// <see cref="IDialogService"/> 구현. 확인창은 셸이 소유한 커스텀 오버레이(평평한 단일 카드)로,
/// 완료 알림은 하단에서 잠시 떴다 사라지는 Snackbar로 보여준다(OS 기본 MessageBox 대체).
/// </summary>
internal sealed class WpfUiDialogService : IDialogService
{
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(2.5);

    private readonly Func<string, string, string, string, Task<bool>> _confirm;
    private readonly Func<string, string, string, string, string, Task<string?>> _prompt;
    private readonly SnackbarService _snackbars = new();

    public WpfUiDialogService(
        Func<string, string, string, string, Task<bool>> confirm,
        Func<string, string, string, string, string, Task<string?>> prompt,
        SnackbarPresenter snackbarHost)
    {
        _confirm = confirm;
        _prompt = prompt;
        _snackbars.SetSnackbarPresenter(snackbarHost);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
        => _confirm(title, message, confirmText, cancelText);

    public Task<string?> PromptAsync(string title, string message, string placeholder,
        string confirmText, string cancelText)
        => _prompt(title, message, placeholder, confirmText, cancelText);

    public void Notify(string title, string message) =>
        _snackbars.Show(title, message, ControlAppearance.Success,
            new SymbolIcon { Symbol = SymbolRegular.CheckmarkCircle24 }, ToastDuration);
}
