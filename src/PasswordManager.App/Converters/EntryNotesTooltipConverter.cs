using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using PasswordManager.Core.Models;

namespace PasswordManager.App.Converters;

/// <summary>
/// 메모 hover 미리보기 툴팁. values[0]=항목(VaultEntry), values[1]=이번 세션 OTP 통과 ID 목록.
/// 메모가 있고 그 항목이 OTP를 통과했을 때만 짧게(최대 120자) 돌려주고, 아니면 null(툴팁 없음).
/// </summary>
public sealed class EntryNotesTooltipConverter : IMultiValueConverter
{
    private const int MaxLength = 120;

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not VaultEntry entry)
            return null;
        if (string.IsNullOrWhiteSpace(entry.Notes))
            return null;

        var verified = values[1] as IEnumerable;
        var granted = verified is not null
            && verified.Cast<object?>().Any(id => string.Equals(id as string, entry.Id, StringComparison.Ordinal));
        if (!granted)
            return null;

        var notes = entry.Notes.Trim();
        return notes.Length > MaxLength ? notes[..MaxLength] + "…" : notes;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
