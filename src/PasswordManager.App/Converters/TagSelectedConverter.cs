using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>
/// 태그 칩의 선택(활성) 상태를 계산한다. values[0]=태그 문자열, values[1]=선택된 태그 목록.
/// 목록에 이 태그가 들어 있으면 true. 칩 ToggleButton의 IsChecked(OneWay)에 바인딩한다.
/// </summary>
public sealed class TagSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string tag || values[1] is not IEnumerable selected)
            return false;
        return selected.Cast<object?>().Any(t => string.Equals(t as string, tag, StringComparison.Ordinal));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
