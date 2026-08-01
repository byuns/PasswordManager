using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PasswordManager.Core.Models;

namespace PasswordManager.App.Converters;

/// <summary>
/// 계정 행의 키보드 선택 강조용. values[0]=이 행의 항목(VaultEntry),
/// values[1]=MainViewModel.SelectedEntry. 같은 항목이면 Visible을 돌려줘
/// 좌측 강조 바·옅은 배경을 켠다(사이드바 현재 섹션 강조와 같은 시각 언어).
/// </summary>
public sealed class EntrySelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Length >= 2
            && values[0] is VaultEntry entry
            && values[1] is VaultEntry current
            && ReferenceEquals(entry, current);

        return selected ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
