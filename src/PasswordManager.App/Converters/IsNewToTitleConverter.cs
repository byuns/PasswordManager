using System.Globalization;
using System.Windows.Data;

namespace PasswordManager.App.Converters;

/// <summary>편집 폼 제목: IsNew(true)→"새 계정", false→"계정 편집".</summary>
public sealed class IsNewToTitleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool isNew && isNew ? "새 계정" : "계정 편집";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
