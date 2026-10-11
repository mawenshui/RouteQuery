using System.Windows;
using RouteQuery.App.ViewModels;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.Web;

/// <summary>
/// 设置与关于窗口。对应 FR-22 / FR-25 / FR-18 可见性 / FR-20。
/// <para>它复用主界面的 <see cref="MainViewModel"/> 而不是再造一个：上限、登录态、码表日期
/// 这些状态本来就活在那边，再造一份就要在两个窗口之间同步，而同步代码是这个项目最不需要的复杂度。</para>
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        new OfficialLoginPage(Vm.LoginPageUrl, Vm.Session, Vm.CookieScopeUrls) { Owner = this }.ShowDialog();
        Vm.NotifySessionChanged();
    }

    private void Logout_Click(object sender, RoutedEventArgs e) => Vm.ClearSession();

    /// <summary>
    /// 切换主题（FR-35）。勾选是只读显示，写档位与换色板都走这一条路径；
    /// 换 <c>MergedDictionaries</c> 是 WPF 动作，所以放在这里而不是视图模型里。
    /// </summary>
    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { Tag: string tag } ||
            !Enum.TryParse<ThemeChoice>(tag, out var choice))
            return;

        Vm.Theme = choice;
        Theme.ThemeManager.Apply(choice);
    }

    /// <summary>清空本机数据必须二次确认（FR-22 验收）。措辞里明确"不能撤销"。</summary>
    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            Composition.Text.Get("设置_清空确认"),
            Composition.Text.Get("设置_清空"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK;
        if (!confirmed) return;

        Composition.ClearLocalData();
        Vm.ShowBookStatus(Composition.Text.Get("设置_已清空"));
    }

    private void Disclaimer_Click(object sender, RoutedEventArgs e) => MessageBox.Show(
        Composition.Text.Get("声明_首次正文"),
        Composition.Text.Get("声明_首次标题"),
        MessageBoxButton.OK,
        MessageBoxImage.Information);
}
