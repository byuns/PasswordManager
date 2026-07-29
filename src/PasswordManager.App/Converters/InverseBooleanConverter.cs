using System.Globalization;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>bool 값을 반전한다. IsBusy → IsEnabled 등에 사용.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
