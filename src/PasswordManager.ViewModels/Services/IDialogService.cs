namespace PasswordManager.ViewModels.Services;

/// <summary>
/// 확인창(예/아니오)과 완료 토스트 같은 사용자 알림을 추상화한다.
/// ViewModel은 이 인터페이스만 의존하고, App 레이어가 WPF-UI로 구현한다(테스트에서는 가짜로 대체).
/// </summary>
public interface IDialogService
{
    /// <summary>확인창을 띄우고 사용자가 '확인'을 누르면 true, 취소면 false를 돌려준다.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);

    /// <summary>하단에 잠시 떴다 자동으로 사라지는 완료 토스트를 표시한다.</summary>
    void Notify(string title, string message);

    /// <summary>
    /// 한 줄 입력을 받는 창을 띄우고 입력값을 돌려준다(취소하면 null). 복구 키처럼 그 자리에서
    /// 받아야 하고 볼트에 저장하지 않는 값에 쓴다(TD-050).
    /// </summary>
    Task<string?> PromptAsync(string title, string message, string placeholder,
        string confirmText, string cancelText);
}
