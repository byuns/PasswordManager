using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PasswordManager.Core.Models;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 언락 후 항목 목록 화면. 로직은 MainViewModel에 있고, 뷰는 카드 리스트 렌더링만 담당한다.
/// 백업/복원 등 보조 동작은 설정 화면(SettingsView)으로 이전했다(design-ux 3절).
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    /// <summary>태그 칩 바는 스크롤바를 숨기므로, 마우스 휠 세로 스크롤을 가로 이동으로 바꿔 슬라이딩한다.</summary>
    private void TagScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TagScroll.ScrollToHorizontalOffset(TagScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// 화면에 들어오면 검색창에 포커스를 준다. 바로 타이핑할 수 있을 뿐 아니라,
    /// UserControl.InputBindings는 포커스가 이 화면 안에 있어야 발동하므로 단축키의 전제이기도 하다.
    /// </summary>
    private void MainView_Loaded(object sender, RoutedEventArgs e) => SearchBox.Focus();

    /// <summary>Ctrl+F: 검색창으로 포커스를 옮기고 기존 검색어를 통째로 선택해 바로 덮어쓸 수 있게 한다.</summary>
    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// 행 아무 곳이나 누르면 그 계정을 선택한다(키보드 ↑↓ 선택과 같은 상태를 공유).
    /// 핀한 계정은 즐겨찾기·사이트 그룹 양쪽에 나오므로, 누른 행이 어느 그룹인지도 함께 알려준다(TD-040).
    /// </summary>
    private void AccountRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not FrameworkElement { DataContext: VaultEntry entry } row)
            return;

        // 이 행을 담은 계정 ItemsControl의 DataContext가 그 행이 속한 그룹이다.
        var group = ItemsControl.ItemsControlFromItemContainer(
            ItemsControl.ContainerFromElement(null, row))?.DataContext as SiteGroup;

        vm.Select(entry, inFavorites: group?.IsFavorites ?? false);
    }
}
