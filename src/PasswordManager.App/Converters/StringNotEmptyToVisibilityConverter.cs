using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>
/// 문자열이 비어있지 않으면 Visible, 비어있으면 Collapsed. ConverterParameter="Invert"면 반대로.
/// 복구 키 표시 여부(생성 폼 ↔ 복구 키 확인 화면) 전환에 사용한다.
/// </summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = !string.IsNullOrEmpty(value as string);
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
            hasText = !hasText;
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
