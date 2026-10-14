using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RouteQuery.App;
using RouteQuery.App.Theme;
using RouteQuery.App.Web;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Ports;

namespace RouteQuery.Tests;

/// <summary>
/// 外观运行时守卫（FR-38）。
/// <para>为什么要有这一条：静态守卫只能证明"键名在代码里存在"，而 <b>ControlTemplate 里的触发器
/// 是第一次应用模板时才构造的</b>——写错时编译能过、解析能过、界面起不来，而且只留下一句
/// "XamlParseException"。本轮改造就是这么把主窗口炸没的（日志里没有内部异常，定位全靠撞）。</para>
/// <para>这里真的把每个换肤控件建出来量一遍，两档色板各一次。它不判断好不好看，
/// 只保证"皮肤不会让窗口打不开"——那正是静态守卫看不见的那一层。</para>
/// </summary>
public class SkinRuntimeGuardTests
{
    private static readonly Dictionary<string, Func<FrameworkElement>> Builders = new()
    {
        ["按钮"] = () => new Button { Content = "查询" },
        ["输入框"] = () => new TextBox { Text = "北京南" },
        ["下拉框"] = () => NewCombo(editable: false),
        ["可编辑下拉框"] = () => NewCombo(editable: true),
        ["日期框"] = () => new DatePicker(),
        ["纵向滚动条"] = () => new ScrollBar(),
        ["横向滚动条"] = () => new ScrollBar { Orientation = Orientation.Horizontal },
        ["列表与行"] = () => new ListBox { ItemsSource = new[] { "北京", "北京南", "北京西" } },
        ["表格与行"] = () => NewGrid(),
        ["页签"] = () => NewTabs(),
        ["分组框"] = () => new GroupBox { Header = "经停站", Content = "内容" },
        ["进度条"] = () => new ProgressBar { Minimum = 0, Maximum = 100, Value = 40 },
        ["单选按钮"] = () => new RadioButton { Content = "只看有票", IsChecked = true },
    };

    private static ComboBox NewCombo(bool editable)
    {
        var box = new ComboBox { ItemsSource = new[] { "高铁/动车", "普速" }, IsEditable = editable };
        if (!editable) box.SelectedIndex = 0;
        return box;
    }

    private static ListView NewGrid()
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "车次", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 80 });
        view.Columns.Add(new GridViewColumn { Header = "发时", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 60 });
        return new ListView { View = view, ItemsSource = new[] { new { Name = "G1" }, new { Name = "G3" } } };
    }

    private static TabControl NewTabs()
    {
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "直达", Content = "列表" });
        tabs.Items.Add(new TabItem { Header = "中转", Content = "卡片" });
        tabs.SelectedIndex = 1;             // 让"选中态"的触发器真的跑一次
        return tabs;
    }

    public static TheoryData<string, ThemeChoice> Cases()
    {
        var data = new TheoryData<string, ThemeChoice>();
        foreach (var name in Builders.Keys)
            foreach (var theme in new[] { ThemeChoice.Light, ThemeChoice.Dark })
                data.Add(name, theme);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void 每个换肤控件在两档色板下都要套上模板(string name, ThemeChoice theme)
    {
        var error = RunOnStaThread(() =>
        {
            ThemeManager.Apply(theme);
            var element = Builders[name]();
            element.Measure(new Size(620, 240));
            element.Arrange(new Rect(0, 0, 620, 240));
            OpenPopupIfAny(element);
            element.UpdateLayout();

            // 判"样式套上了没有"，不判尺寸：滚动条、下拉框这类控件不自我定宽，
            // 断言 DesiredSize 非零会把好代码判成红（第一版就是这么误报 16 条的）。
            if (element is Control control)
            {
                // 隐式样式的自动解析要依赖 Application 与所在线程的 Dispatcher，测试里这层不确定；
                // 而本轮要验的不是"查找机制"，是**模板内容会不会在应用时抛**。所以直接从字典取那条样式套上去。
                control.Style = ImplicitStyleFor(control)
                    ?? throw new InvalidOperationException($"{name} 在 {theme} 档下字典里没有它的样式");
                control.Measure(new Size(620, 240));
                control.UpdateLayout();
                // 只有"我们的样式自己给了模板"的控件才要求套上；没给的（列表、进度条）继续用主题的模板，
                // 在那里断言非空等于测了个不存在的东西。
                var 我们给了模板 = control.Style!.Setters.OfType<Setter>()
                    .Any(s => s.Property == Control.TemplateProperty);
                if (我们给了模板 && control.Template is null)
                    throw new InvalidOperationException($"{name} 在 {theme} 档下样式里的模板没套上");
            }
        });

        Assert.True(error is null, $"{name}（{theme} 档）失败：{Flatten(error)}");
    }

    // ── 离屏渲染：把"皮肤真的画出来了"变成断言 ────────────────────────
    //
    // 为什么还要这一条：上面的守卫能证明"模板套得上"，静态守卫能证明"色板里有渐变键"，
    // 但有两种失败它们都看不见——渐变被别的纯色背景盖掉，以及前景色与底色撞成同一个色，
    // 结果都是"代码改了、界面看着还是一张纯色 / 一片糊"。我自己隔着屏幕看不到
    // （这台机器的窗口置不到前台），所以让测试替我把它渲染出来数颜色，并把 PNG 落盘供人肉眼复核。
    // 注意：这三条判的是"画没画出来"，不判"数据绑没绑上"——界面本来就有一堆静态标签，
    // 空壳与有数据的差别不足以撑起一条断言，数据是否显示归视图模型那批测试管。
    /// <summary>取样列：两个窗口的内容都有 16 像素外边距，第 8 列只可能是窗口底。</summary>
    private const int 窗口底取样列 = 8;

    [Theory]
    [InlineData(ThemeChoice.Light, "直达")]
    [InlineData(ThemeChoice.Dark, "直达")]
    [InlineData(ThemeChoice.Light, "中转")]
    [InlineData(ThemeChoice.Dark, "中转")]
    [InlineData(ThemeChoice.Light, "报错")]
    [InlineData(ThemeChoice.Dark, "报错")]
    [InlineData(ThemeChoice.Light, "设置")]
    [InlineData(ThemeChoice.Dark, "设置")]
    public void 每个界面在两档色板下都渲染出渐变皮肤(ThemeChoice theme, string view)
    {
        var error = RunOnStaThread(() =>
        {
            ThemeManager.Apply(theme);
            var (窗口底, 内容, 宽, 高) = 装好数据的界面(view);

            // 窗口自己的背景不在内容可视树里（没 Show 出来的 Window 渲染不出边框底色），
            // 所以把同一支画刷挂到宿主 Border 上——用的还是界面里那支 渐变_窗口底，
            // 不复制键名、也不复制色值。
            var host = new Border { Width = 宽, Height = 高, Background = 窗口底, Child = 内容 };
            if (view == "中转") 切到中转页(host);

            var bmp = 渲染(host, 宽, 高);
            var (全图色数, 窗口底色数, 前景像素数) = 数颜色(bmp, 宽, 高);
            存图(bmp, theme, view);

            if (全图色数 < 24)
                throw new InvalidOperationException($"整幅只数出 {全图色数} 种颜色——界面根本没渲染出来");
            // 一条竖线上只有 1 种颜色，就说明窗口底是纯色：渐变没生效。
            if (窗口底色数 < 4)
                throw new InvalidOperationException($"窗口底这一列只有 {窗口底色数} 种颜色，渐变没画出来（渲染成了纯色）");
            // 实测数字：正常渲染 3.0~3.6 万；把正文色换成接近底色仍有 2 万
            // （次级文字、边框、有票色都还在）。所以这条兜不住"某一支笔刷撞色"——那归静态对比度守卫管，
            // 它能精确到具体哪一支。这里只兜"整片一个字都没画出来"这种粗失败，门槛因此压得很低。
            if (前景像素数 < 8000)
                throw new InvalidOperationException($"跟底色差得远的像素只有 {前景像素数} 个——字没画出来，界面糊成一片了");
        });

        Assert.True(error is null, $"{view}（{theme} 档）渲染失败：{Flatten(error)}");
    }

    /// <summary>
    /// 造一个带数据的界面。四个视图各挑一次：结果行、中转分段展开、错误横幅（FR-38 要求语义色不被稀释）、
    /// 以及设置页——它是亲友第二常打开的窗口，里面的下拉框与开关走的是另一批模板。
    /// </summary>
    private static (Brush? 窗口底, FrameworkElement 内容, double 宽, double 高) 装好数据的界面(string view)
    {
        var h = new MainViewModelTests.Harness();
        h.Ready();
        Window window;

        switch (view)
        {
            case "直达":
                h.Query.Result = [MainViewModelTests.Journey("G1"), MainViewModelTests.Journey("G3"), MainViewModelTests.Journey("G7")];
                h.Vm.QueryAsync(default).GetAwaiter().GetResult();
                h.Vm.Selected = h.Vm.Rows[0];
                h.Vm.LoadStopsAsync(default).GetAwaiter().GetResult();
                window = new MainWindow { DataContext = h.Vm };
                break;

            case "中转":
                h.Transfer.Result = new TransferQueryResult([MainViewModelTests.TwoTrainPlan()], 0, 0);
                h.Vm.RunTransferAsync(default).GetAwaiter().GetResult();
                h.Vm.LoadLegStopsAsync(h.Vm.Transfers[0].Legs[0], default).GetAwaiter().GetResult();
                window = new MainWindow { DataContext = h.Vm };
                break;

            case "报错":
                h.Query.Throws = new QueryException(QueryErrorKind.Timeout);
                h.Vm.QueryAsync(default).GetAwaiter().GetResult();
                window = new MainWindow { DataContext = h.Vm };
                break;

            default:
                window = new SettingsWindow { DataContext = h.Vm };
                break;
        }

        var root = (FrameworkElement)window.Content;
        window.Content = null;          // 先脱离 Window，否则挂到宿主 Border 时会报"已有逻辑父级"
        root.DataContext = h.Vm;        // 脱离后继承链就断了：不补这一句，渲染出来的是没有数据的空壳
        return (window.Background, root, window.Width, window.Height);
    }

    private static void 切到中转页(FrameworkElement host)
    {
        var tabs = 找首个<TabControl>(host) ?? throw new InvalidOperationException("界面里没找到页签控件");
        tabs.SelectedIndex = 1;
        布局(host, host.Width, host.Height);
    }

    private static void 布局(FrameworkElement host, double 宽, double 高)
    {
        host.Measure(new Size(宽, 高));
        host.Arrange(new Rect(0, 0, 宽, 高));
        host.UpdateLayout();
    }

    private static RenderTargetBitmap 渲染(FrameworkElement host, double 宽, double 高)
    {
        布局(host, 宽, 高);
        var bmp = new RenderTargetBitmap((int)宽, (int)高, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        return bmp;
    }

    private static T? 找首个<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (找首个<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private static (int 全图色数, int 窗口底色数, int 前景像素数) 数颜色(RenderTargetBitmap bmp, double 宽, double 高)
    {
        var (w, h) = ((int)宽, (int)高);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);

        var 出现次数 = new Dictionary<uint, int>();
        for (var i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            var 色 = BitConverter.ToUInt32(px, i);
            出现次数[色] = 出现次数.TryGetValue(色, out var n) ? n + 1 : 1;
        }

        // 整幅里出现最多的那个色就是"底"。跟它差得远的像素只可能是文字、图标或边界——
        // 一个都没有，说明前景色跟底色撞上了（皮肤糊成一片），界面等于没字。
        var 底色 = 出现次数.MaxBy(p => p.Value).Key;
        var (b, g, r) = ((byte)(底色 >> 16), (byte)(底色 >> 8), (byte)底色);
        var 前景 = 0;
        foreach (var 色 in 出现次数.Keys)
        {
            var 距 = Math.Max(Math.Abs((byte)(色 >> 16) - b), Math.Max(Math.Abs((byte)(色 >> 8) - g), Math.Abs((byte)色 - r)));
            if (距 > 60) 前景 += 出现次数[色];
        }

        // 只看第 8 列：它是窗口底，不受任何控件遮挡，纯色与渐变的差别在这里最干净。
        var 竖线 = new HashSet<uint>();
        for (var y = 0; y < h; y++)
        {
            var o = y * w * 4 + 窗口底取样列 * 4;
            if (px[o + 3] != 0) 竖线.Add(BitConverter.ToUInt32(px, o));
        }

        return (出现次数.Count, 竖线.Count, 前景);
    }

    /// <summary>把渲染结果落盘供肉眼复核。写失败不判红——守卫的本职是数颜色，不是发图。</summary>
    private static void 存图(RenderTargetBitmap bmp, ThemeChoice theme, string view)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "rq-skin-render");
            Directory.CreateDirectory(dir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(Path.Combine(dir, $"{theme}-{view}.png"));
            encoder.Save(file);
        }
        catch (Exception)
        {
            // 故意忽略
        }
    }

    /// <summary>弹层里的模板只在打开那一刻才构造，所以必须真开一次：静态守卫与"只测量"都看不见它。</summary>
    private static void OpenPopupIfAny(FrameworkElement element)
    {
        switch (element)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = true;
                break;
            case DatePicker picker:
                picker.IsDropDownOpen = true;
                break;
            case TabControl tabs:
                foreach (TabItem item in tabs.Items) item.IsSelected = true;
                break;
            case ListBox list:
                list.SelectedIndex = list.Items.Count - 1;
                break;
            // ListView 本身就是 ListBox 的子类，上面那条已经覆盖它了
        }
        element.UpdateLayout();
    }

    private static Style? ImplicitStyleFor(Control control) =>
        Application.Current.Resources[control.GetType()] as Style;

    private static Exception? RunOnStaThread(Action body)
    {
        Exception? caught = null;

        // WPF 对象只能在 STA 线程上创建，xUnit 的工作线程是 MTA；Application 也只能建一次。
        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application();
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add( dictionary("Resources/文案.xaml"));
                    app.Resources.MergedDictionaries.Add(dictionary("Resources/主题.xaml"));
                }
                body();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return caught;
    }

    private static ResourceDictionary dictionary(string relative) =>
        new() { Source = new Uri($"pack://application:,,,/RouteQuery;component/{relative}", UriKind.Absolute) };

    private static string Flatten(Exception? ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        return parts.Count == 0 ? "无" : string.Join(" ← ", parts);
    }
}
