using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RouteQuery.App.ViewModels;
using RouteQuery.Core.Model;

namespace RouteQuery.App;

/// <summary>
/// 主窗口代码后置。<b>只放控件级交互</b>（焦点、把方向键转成候选列表的选择、弹层开合），
/// 不放任何业务判断——那是 SPEC-004 五.1 的边界。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Swap_Click(object sender, RoutedEventArgs e) => Vm.Swap();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Vm.CancelQuery();

    /// <summary>打开内嵌官方登录页。窗口关掉后一律刷新一次状态——
    /// 用户可能在里面登录了、也可能什么都没做，两种情况界面都要如实反映。</summary>
    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var page = new Web.OfficialLoginPage(Vm.LoginPageUrl, Vm.Session) { Owner = this };
        page.ShowDialog();
        Vm.NotifySessionChanged();
    }

    private void Logout_Click(object sender, RoutedEventArgs e) => Vm.ClearSession();

    // ── 侧栏：收藏与历史（FR-16 / FR-17）────────────────────
    private void SaveRoute_Click(object sender, RoutedEventArgs e) => Vm.SaveCurrentRoute();

    private void ClearHistory_Click(object sender, RoutedEventArgs e) => Vm.ClearHistory();

    /// <summary>双击即回填条件。回填<b>不自动查询</b>：日期必须由用户重新确认（FR-16）。</summary>
    private void Saved_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: Core.Ports.SavedRoute route }) Vm.ApplySaved(route);
    }

    private void History_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ViewModels.HistoryRowViewModel row }) Vm.ApplyHistory(row);
    }

    private void QuickDate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var offset)) Vm.QuickDate(offset);
    }

    /// <summary>点别处就收起候选——候选是输入辅助，常驻会挡住下面的列表。</summary>
    private void Box_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;

        // 焦点挪到候选列表上不收——否则鼠标永远点不到候选项。
        var list = ReferenceEquals(box, FromBox) ? FromList : ToList;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!box.IsKeyboardFocusWithin && list is not { IsKeyboardFocusWithin: true }) Vm.CloseCandidates();
        }));
    }

    private void FromBox_PreviewKeyDown(object sender, KeyEventArgs e) =>
        HandlePickerKey(FromList, e, Vm.FromCandidates);

    private void ToBox_PreviewKeyDown(object sender, KeyEventArgs e) =>
        HandlePickerKey(ToList, e, Vm.ToCandidates);

    /// <summary>
    /// 方向键只负责"把高亮移到哪一条"，<b>提交由 SelectedItem 绑定完成</b>（见 MainViewModel.FromPick）。
    /// 回车与 Esc 收起弹层。这样鼠标点选与键盘选择走的是同一条提交路径。
    /// </summary>
    private void HandlePickerKey(ListBox? list, KeyEventArgs e, ObservableCollection<Station> candidates)
    {
        switch (e.Key)
        {
            case Key.Down when list is { Items.Count: > 0 }:
                Move(list, +1);
                e.Handled = true;
                break;

            case Key.Up when list is { Items.Count: > 0 }:
                Move(list, -1);
                e.Handled = true;
                break;

            case Key.Enter or Key.Escape:
                candidates.Clear();
                e.Handled = true;
                break;
        }
    }

    /// <summary>循环滚动：到底了再往下就回到第一条——查热门线路时不用来回拖滚动条。</summary>
    private static void Move(ListBox list, int delta)
    {
        var count = list.Items.Count;
        var next = (list.SelectedIndex + delta + count) % count;
        list.SelectedIndex = next;
        list.ScrollIntoView(list.SelectedItem);
    }
}
