using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>
/// 개수(0보다 큰 정수)면 Visible, 0이면 Collapsed. 비밀번호 이력 목록처럼 항목이 있을 때만
/// 섹션을 보이는 데 사용한다. ConverterParameter="Invert"면 반대로.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value is int count && count > 0;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            hasItems = !hasItems;
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
