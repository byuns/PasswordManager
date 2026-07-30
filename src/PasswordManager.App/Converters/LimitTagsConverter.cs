using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>
/// 태그 목록에서 앞쪽 최대 N개(기본 2)만 잘라 돌려준다(목록에 태그 1~2개만 노출). ConverterParameter로
/// 개수를 바꿀 수 있다. 목록이 비었거나 null이면 빈 시퀀스.
/// </summary>
public sealed class LimitTagsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var max = 2;
        if (parameter is string s && int.TryParse(s, out var n))
            max = n;
        if (value is IEnumerable items and not string)
            return items.Cast<object>().Take(max).ToList();
        return new List<object>();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
