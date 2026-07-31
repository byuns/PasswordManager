using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using PasswordManager.Core.Models;

namespace PasswordManager.App.Converters;

/// <summary>
/// 행별 '인증' 버튼 ↔ 보기/편집/삭제 3버튼 전환용. values[0]=항목(VaultEntry),
/// values[1]=이번 세션 OTP 통과 ID 목록. ConverterParameter가 "unverified"면 아직 통과하지
/// 않은 항목에서 Visible(인증 버튼용), 그 외("verified"/기본)면 통과한 항목에서 Visible(3버튼용).
/// </summary>
public sealed class EntryVerifiedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var granted = values.Length >= 2
            && values[0] is VaultEntry entry
            && values[1] is IEnumerable verified
            && verified.Cast<object?>().Any(id => string.Equals(id as string, entry.Id, StringComparison.Ordinal));

        var wantUnverified = string.Equals(parameter as string, "unverified", StringComparison.Ordinal);
        var visible = wantUnverified ? !granted : granted;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
