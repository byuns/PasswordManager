using System.Globalization;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>
/// 문자열의 첫 글자를 대문자로 반환한다(카드 아바타의 사이트 이니셜, design-ux §4).
/// 비어 있으면 물음표를 돌려준다.
/// </summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.TrimStart();
        return string.IsNullOrEmpty(text) ? "?" : char.ToUpper(text[0], culture).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
