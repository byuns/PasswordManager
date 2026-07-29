namespace PasswordManager.ViewModels.Services;

/// <summary>클립보드 접근 추상화. 플랫폼 구현(WPF)은 App 계층에 둔다(테스트에서는 가짜 주입).</summary>
public interface IClipboardService
{
    /// <summary>클립보드에 텍스트를 넣는다.</summary>
    void SetText(string text);

    /// <summary>클립보드를 비운다.</summary>
    void Clear();
}
