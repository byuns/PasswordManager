using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    /// 태그 칩이 바를 넘칠 때만 오른쪽 페이드를 씌운다(TD-048). 안 넘치면 가릴 것이 없는데도
    /// 마지막 칩이 흐려 보이므로 마스크를 걷는다. 칩이 늘거나 창 폭이 바뀌면 extent/viewport가
    /// 달라지며 이 이벤트가 다시 오므로, 별도 SizeChanged 없이 여기 한 곳에서 갱신한다.
    /// </summary>
    private void TagScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        TagScroll.OpacityMask = TagScroll.ScrollableWidth > 0
            ? (Brush)TagScroll.FindResource("TagFadeMask")
            : null;

    /// <summary>
    /// 화면에 들어오면 검색창에 포커스를 준다. 바로 타이핑할 수 있을 뿐 아니라,
    /// UserControl.InputBindings는 포커스가 이 화면 안에 있어야 발동하므로 단축키의 전제이기도 하다.
    /// </summary>
    private void MainView_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        // 셸이 화면을 갈아끼우는 구조라 OTP 게이트·보기·편집에서 돌아오면 이 View가 새로 만들어지고
        // 목록이 맨 위로 되돌아간다. 선택된 행이 있으면 그 자리로 스크롤해 방금 다루던 계정을
        // 다시 보여준다(TD-049). 행 컨테이너는 렌더 뒤에야 생기므로 한 틱 미룬다.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(BringSelectionIntoView));
    }

    /// <summary>선택된 계정의 행을 화면 안으로 스크롤한다. 이미 보이면 아무 일도 하지 않는다.</summary>
    private void BringSelectionIntoView()
    {
        if (DataContext is not MainViewModel { SelectedEntry: { } selected } vm)
            return;
        FindRow(GroupList, selected, vm.SelectionInFavorites)?.BringIntoView();
    }

    /// <summary>
    /// 선택된 계정의 행 요소를 시각 트리에서 찾는다. 핀한 계정은 즐겨찾기·사이트 그룹 양쪽에
    /// 같은 인스턴스로 나오므로, 어느 그룹의 행인지까지 맞춰야 엉뚱한 쪽으로 스크롤하지 않는다(TD-040).
    /// </summary>
    private static FrameworkElement? FindRow(DependencyObject root, VaultEntry target, bool inFavorites)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { DataContext: VaultEntry entry } row && ReferenceEquals(entry, target))
            {
                var group = ItemsControl.ItemsControlFromItemContainer(
                    ItemsControl.ContainerFromElement(null, row))?.DataContext as SiteGroup;
                if ((group?.IsFavorites ?? false) == inFavorites)
                    return row;
            }

            if (FindRow(child, target, inFavorites) is { } found)
                return found;
        }
        return null;
    }

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
