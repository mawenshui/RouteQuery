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
        DataContextChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    // ── 面板显隐与几何（FR-40）──────────────────────────────────
    //
    // 为什么放在代码后置而不是 XAML：隐藏一块区域要把它那一列/那一行的尺寸也收掉，
    // 而 ColumnDefinition.Width 与 RowDefinition.Height 不是能直接绑定的东西；
    // 更关键的是"最小尺寸"——侧栏平时最小 160，隐藏时若不清零就永远收不起来。
    // 视图模型只管"显示还是不显示"这个布尔值，几何是界面层的事。
    private GridLength _侧栏宽 = new(210);
    private GridLength _详情高 = new(280);
    private MainViewModel? _tracked;

    private void Sync()
    {
        if (ReferenceEquals(_tracked, Vm)) return;
        if (_tracked is not null) _tracked.PropertyChanged -= Tracked_Changed;
        _tracked = Vm;
        _tracked.PropertyChanged += Tracked_Changed;
        ApplyGeometry();
    }

    private void Tracked_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowSidebar) or nameof(MainViewModel.ShowDetail))
            ApplyGeometry();
    }

    private void ApplyGeometry()
    {
        if (_tracked is not { } vm) return;

        if (vm.ShowSidebar)
        {
            BodyGrid.ColumnDefinitions[0].MinWidth = 160;
            BodyGrid.ColumnDefinitions[0].Width = _侧栏宽;
        }
        else
        {
            if (BodyGrid.ColumnDefinitions[0].Width.Value > 1) _侧栏宽 = BodyGrid.ColumnDefinitions[0].Width;
            BodyGrid.ColumnDefinitions[0].MinWidth = 0;
            BodyGrid.ColumnDefinitions[0].Width = new GridLength(0);
        }

        if (vm.ShowDetail)
        {
            MainGrid.RowDefinitions[4].MinHeight = 120;
            MainGrid.RowDefinitions[4].Height = _详情高;
        }
        else
        {
            if (MainGrid.RowDefinitions[4].Height.Value > 1) _详情高 = MainGrid.RowDefinitions[4].Height;
            MainGrid.RowDefinitions[4].MinHeight = 0;
            MainGrid.RowDefinitions[4].Height = new GridLength(0);
        }
    }

    private void ToggleDetail_Click(object sender, RoutedEventArgs e) => Vm.ShowDetail = !Vm.ShowDetail;

    // ── 顶部菜单（FR-41）───────────────────────────────────────
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowStatement_Click(object sender, RoutedEventArgs e) => MessageBox.Show(
        Composition.Text.Get("声明_首次正文"),
        Composition.Text.Get("声明_首次标题"),
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    /// <summary>菜单里的主题三档与设置页里那三个单选按钮改的是同一个 <see cref="MainViewModel.Theme"/>，
    /// 换色板是 WPF 动作，所以由界面层调 ThemeManager。</summary>
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse<Core.Ports.ThemeChoice>(tag, out var choice))
            return;

        Vm.Theme = choice;
        Theme.ThemeManager.Apply(choice);
    }

    /// <summary>更新站点码表（FR-18）。成功后必须弹一次重启提示——更新完不重启等于没更新，
    /// 而写在侧栏里的那一句会被划过去。失败不弹，也绝不显示成功措辞。</summary>
    private async void UpdateStations_Click(object sender, RoutedEventArgs e)
    {
        await Vm.UpdateStationsAsync(default);
        if (!Vm.StationUpdateNeedsRestart) return;

        Vm.AcknowledgeStationUpdate();
        var now = MessageBox.Show(
            Vm.StationUpdateNote,
            Composition.Text.Get("码表_重启标题"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (now != MessageBoxResult.Yes) return;

        // 先起新进程再退当前：反过来会出现"双击没反应、程序也没了"的观感，
        // 而单文件自包含件冷启动要十几秒，用户会以为它崩了。
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = Environment.ProcessPath!, UseShellExecute = true });
        }
        catch (Exception)
        {
            MessageBox.Show(Composition.Text.Get("错误_数据缺失"),
                Composition.Text.Get("码表_重启标题"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Application.Current.Shutdown();
    }

    // ── 整块可展开（FR-39）─────────────────────────────────────
    /// <summary>点站名框的空白处重新铺开候选。打字路径不变，所以这里只补"点了却什么都没出现"那一下。</summary>
    private void PickerBox_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (ReferenceEquals(box, FromBox)) Vm.ReopenFromCandidates();
        else Vm.ReopenToCandidates();
    }

    /// <summary>日期框点任意位置弹出日历（FR-39）。箭头那一下由控件自己负责，
    /// 这里再翻一次会变成"点了等于没点"，所以命中箭头时直接放过。</summary>
    private void DateBox_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton) return;
        DateBox.IsDropDownOpen = true;
    }


    private void Swap_Click(object sender, RoutedEventArgs e) => Vm.Swap();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Vm.CancelQuery();

    /// <summary>打开内嵌官方登录页。窗口关掉后一律刷新一次状态——
    /// 用户可能在里面登录了、也可能什么都没做，两种情况界面都要如实反映。</summary>
    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var page = new Web.OfficialLoginPage(Vm.LoginPageUrl, Vm.Session, Vm.CookieScopeUrls) { Owner = this };
        page.ShowDialog();
        Vm.NotifySessionChanged();
    }

    private void Logout_Click(object sender, RoutedEventArgs e) => Vm.ClearSession();

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new Web.SettingsWindow { Owner = this, DataContext = Vm }.ShowDialog();

    /// <summary>打开发布页（FR-36）。地址来自数据层的仓库常量，不来自响应内容；本应用同样不发这个请求。</summary>
    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = Vm.ReleasePageUrl, UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开就不说：这条提示的存在与否都不该影响用户查车次。
        }
    }

    // ── 侧栏：收藏与历史（FR-16 / FR-17）────────────────────
    private void SaveRoute_Click(object sender, RoutedEventArgs e) => Vm.SaveCurrentRoute();

    private void ClearHistory_Click(object sender, RoutedEventArgs e) => Vm.ClearHistory();

    /// <summary>删除这一条收藏。删完必须把"删了哪条"说出来——侧栏里没有别的反馈位置。</summary>
    private void RemoveSaved_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Core.Ports.SavedRoute route }) Vm.RemoveSaved(route.Name);
    }

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
        list.SelectedIndex = NextIndex(list.SelectedIndex, list.Items.Count, delta);
        list.ScrollIntoView(list.SelectedItem);
    }

    /// <summary>
    /// 一次方向键之后高亮落在哪一条。弹层刚打开时没有任何预选（<c>current == -1</c>），
    /// 所以<b>第 n 次按 ↓ 高亮第 n 条</b>，"↓ Enter"就是选第一条——这是界面说明第五节那条
    /// 键盘主链路的由来，也是用例 TC-A4 的判据出处。单独拆出来是为了让这条差一能被断言，
    /// 而不是靠真键盘去赌按键有没有送达。
    /// </summary>
    internal static int NextIndex(int current, int count, int delta) =>
        (current + delta + count) % count;
}
