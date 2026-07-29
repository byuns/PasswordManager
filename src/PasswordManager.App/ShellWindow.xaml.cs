using Wpf.Ui.Controls;

namespace PasswordManager.App;

/// <summary>앱의 최상위 창. DataContext(ShellViewModel)의 CurrentViewModel을 DataTemplate로 렌더링한다.</summary>
public partial class ShellWindow : FluentWindow
{
    public ShellWindow() => InitializeComponent();
}
