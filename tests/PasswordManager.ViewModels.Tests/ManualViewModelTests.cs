using PasswordManager.ViewModels;

namespace PasswordManager.ViewModels.Tests;

public class ManualViewModelTests
{
    [Fact]
    public void Sections_are_ordered_from_getting_started_to_security()
    {
        var vm = new ManualViewModel();

        Assert.Equal(
            new[] { "시작하기", "매일 쓰기", "보안·복구" },
            vm.Sections.Select(s => s.Title));
    }

    [Fact]
    public void Every_section_has_an_icon_and_at_least_one_item()
    {
        var vm = new ManualViewModel();

        Assert.All(vm.Sections, section =>
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Icon));
            Assert.NotEmpty(section.Items);
        });
    }

    [Fact]
    public void Every_item_has_icon_title_and_description()
    {
        var vm = new ManualViewModel();

        Assert.All(vm.Sections.SelectMany(s => s.Items), item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Icon));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
        });
    }

    [Fact]
    public void Shortcuts_cover_the_seven_keys_bound_in_MainView()
    {
        var vm = new ManualViewModel();

        // MainView.xaml의 InputBindings와 같은 7개(design-ux 5절·TD-039).
        var keys = vm.Shortcuts.Select(s => string.Join("+", s.Keys)).ToList();

        Assert.Equal(7, vm.Shortcuts.Count);
        Assert.Contains("Ctrl+F", keys);
        Assert.Contains("Ctrl+N", keys);
        Assert.Contains("Ctrl+L", keys);
        Assert.Contains("Ctrl+B", keys);
        Assert.Contains("Esc", keys);
        Assert.Contains("Enter", keys);
        Assert.Contains("↑+↓", keys); // 한 줄에 두 키를 나란히 보여준다
    }

    [Fact]
    public void Every_shortcut_has_keys_and_a_description()
    {
        var vm = new ManualViewModel();

        Assert.All(vm.Shortcuts, s =>
        {
            Assert.NotEmpty(s.Keys);
            Assert.All(s.Keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }
}
