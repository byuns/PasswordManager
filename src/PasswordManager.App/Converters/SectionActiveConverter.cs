using System.Globalization;
using System.Windows.Data;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Converters;

/// <summary>
/// 사이드바 항목이 현재 보고 있는 섹션인지 계산한다.
/// value=<see cref="ShellSection"/>?(ShellViewModel.Section), parameter=항목이 가리키는 섹션 이름("Items" 등).
/// 사이드 메뉴 버튼의 Tag(OneWay)에 물려 선택 강조 트리거를 켠다.
/// </summary>
public sealed class SectionActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ShellSection section
           && parameter is string name
           && Enum.TryParse<ShellSection>(name, out var target)
           && section == target;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
