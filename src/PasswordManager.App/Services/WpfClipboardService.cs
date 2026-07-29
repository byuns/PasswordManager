using System.Windows;
using PasswordManager.ViewModels.Services;

namespace PasswordManager.App.Services;

/// <summary>WPF 클립보드 기반 <see cref="IClipboardService"/> 구현. 클립보드 접근은 간헐적으로
/// 실패(COM 잠금)할 수 있어 예외를 삼킨다 — 위생 기능이라 실패가 앱을 막아선 안 된다.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        try { Clipboard.SetText(text); }
        catch { /* 클립보드 일시 잠금 무시 */ }
    }

    public void Clear()
    {
        try { Clipboard.Clear(); }
        catch { /* 클립보드 일시 잠금 무시 */ }
    }
}
