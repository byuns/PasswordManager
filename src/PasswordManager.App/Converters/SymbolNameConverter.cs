using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace PasswordManager.App.Converters;

/// <summary>
/// 아이콘 이름 문자열(예: "Key24")을 WPF-UI <see cref="SymbolRegular"/>로 바꾼다.
/// ViewModel(ManualViewModel)이 WPF-UI에 의존하지 않고도 아이콘을 지정할 수 있게 하는 다리.
/// 모르는 이름이면 <see cref="SymbolRegular.Empty"/>로 떨어뜨려 렌더링을 깨뜨리지 않는다.
/// </summary>
public sealed class SymbolNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Enum.TryParse<SymbolRegular>(value as string, out var symbol) ? symbol : SymbolRegular.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
