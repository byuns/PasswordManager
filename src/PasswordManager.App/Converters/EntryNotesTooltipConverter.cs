using System.Globalization;
using System.Windows.Data;
using PasswordManager.Core.Models;

namespace PasswordManager.App.Converters;

/// <summary>
/// 메모 hover 미리보기 툴팁. 메모는 민감정보를 담지 않는다는 전제(편집 폼 경고)하에 OTP 없이도
/// 바로 보여준다. 메모가 있으면 짧게(최대 120자) 돌려주고, 없으면 null(툴팁 없음).
/// </summary>
public sealed class EntryNotesTooltipConverter : IValueConverter
{
    private const int MaxLength = 120;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not VaultEntry entry || string.IsNullOrWhiteSpace(entry.Notes))
            return null;

        var notes = entry.Notes.Trim();
        return notes.Length > MaxLength ? notes[..MaxLength] + "…" : notes;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
