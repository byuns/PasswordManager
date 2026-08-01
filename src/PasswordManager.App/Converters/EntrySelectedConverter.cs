using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PasswordManager.Core.Models;

namespace PasswordManager.App.Converters;

/// <summary>
/// 계정 행의 키보드 선택 강조용. values[0]=이 행의 항목(VaultEntry),
/// values[1]=MainViewModel.SelectedEntry, values[2]=이 행이 속한 그룹이 즐겨찾기인가,
/// values[3]=MainViewModel.SelectionInFavorites.
/// <para>
/// 핀한 계정은 즐겨찾기 그룹과 사이트 그룹에 **같은 인스턴스**로 두 번 나오므로 항목만 비교하면
/// 두 행이 동시에 강조된다. 그래서 "어느 그룹의 행인지"까지 일치해야 강조를 켠다(TD-040).
/// </para>
/// </summary>
public sealed class EntrySelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 4
            || values[0] is not VaultEntry entry
            || values[1] is not VaultEntry current
            || !ReferenceEquals(entry, current))
            return Visibility.Collapsed;

        // 바인딩이 아직 안 붙었으면(UnsetValue) false로 본다 — 사이트 그룹이 기본이다.
        var rowInFavorites = values[2] as bool? ?? false;
        var selectionInFavorites = values[3] as bool? ?? false;

        return rowInFavorites == selectionInFavorites ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
