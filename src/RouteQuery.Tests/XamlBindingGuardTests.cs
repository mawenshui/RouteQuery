using System.IO;
using System.Text.RegularExpressions;
using RouteQuery.App.ViewModels;

namespace RouteQuery.Tests;

/// <summary>
/// 界面绑定与文案键的静态守卫。
/// <para>为什么用"读源文件"的方式测：WPF 的绑定写错属性名、或者引用一条不存在的文案，运行时既不抛异常
/// 也不留下任何痕迹——界面上就是一片空白，而空白长得跟"没问题"一模一样。这一类问题在同一天里靠人眼
/// 抓到了两次（关于页的版本号绑了一个视图模型上根本不存在的属性；跨日标注做出来了但列表模板没绑它），
/// 而抓的时候屏幕正被锁住、没法走查。</para>
/// <para>它只能证明"这个名字在代码里存在"，不能证明"绑在了正确的那一层"——后者仍然要靠界面走查。</para>
/// </summary>
public class XamlBindingGuardTests
{
    private static readonly string AppDir = Path.Combine(Samples.RepoRoot, "src", "RouteQuery.App");

    [Fact]
    public void XAML里绑定的属性名必须在代码里真实存在()
    {
        var known = PublicPropertyNames();
        var missing = new SortedSet<string>();

        foreach (var (file, text) in Tagged(AppDir, "*.xaml"))
            foreach (var (path, exempt) in Bindings(text))
            {
                if (exempt) continue;               // 见 TemplatedBindingPattern 的说明
                // 只查根名：Selected.Train 的根是 MainViewModel.Selected，Train 由它自己那一层解析。
                var root = path.Split('.')[0];
                if (!known.Contains(root)) missing.Add($"{file}:{root}");
            }

        Assert.Empty(missing);
    }

    /// <summary>
    /// 表头与数据行的列宽必须同源（FR-42）。
    /// <para>为什么单独守这一条：表头那一行画在 ScrollViewer 外面（这样它才不随纵向滚动滚走），
    /// 于是它的列宽与行模板的列宽是**两处各写一遍的数字**。这种形态一定会漂——
    /// 漂了不报错，只是表头与数据对不齐，而"看起来对齐"正是它最难发现的地方。</para>
    /// </summary>
    [Fact]
    public void 结果列表的表头列宽必须与行模板逐列相同()
    {
        var text = File.ReadAllText(Path.Combine(AppDir, "MainWindow.xaml"));
        var start = text.IndexOf("<ListView x:Name=\"ResultList\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "主窗口里找不到结果列表，这条守卫就变成空转了");
        var list = text[start..text.IndexOf("</ListView>", start, StringComparison.Ordinal)];

        var 表头 = 列宽(list, "<ListView.Template>", "<ListView.ItemContainerStyle>");
        var 行 = 列宽(list, "<ListView.ItemTemplate>", "</ListView>");

        Assert.True(表头.Count > 0 && 行.Count > 0, $"表头或行模板没找到列定义（表头 {表头.Count} 列，行 {行.Count} 列）");
        Assert.Equal(行, 表头);
    }

    private static List<string> 列宽(string text, string from, string until)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, $"找不到 {from}");
        var j = text.IndexOf(until, i, StringComparison.Ordinal);
        var seg = j < 0 ? text[i..] : text[i..j];
        return System.Text.RegularExpressions.Regex.Matches(seg, @"<ColumnDefinition Width=""([^""]+)""/>")
            .Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void 引用的每一条文案键都必须有定义()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, text) in Tagged(AppDir, "*.xaml"))
            foreach (var key in Names(text, KeyPattern))
                defined.Add(key);

        var used = new SortedSet<string>();
        foreach (var (file, text) in Tagged(AppDir, "*.xaml"))
            foreach (var key in Names(text, DynamicPattern))
                used.Add($"{key}  ←{file}");
        foreach (var (file, text) in Tagged(AppDir, "*.cs"))
            foreach (var key in Names(text, GetPattern))
                used.Add($"{key}  ←{file}");

        var missing = used.Where(k => !defined.Contains(k.Split("  ←")[0])).ToList();

        Assert.Empty(missing);
    }

    /// <summary>MatchCollection 的 foreach 走的是旧的非泛型枚举器，拿到的元素是 object，所以统一用 Select 取第一个捕获组。</summary>
    private static IEnumerable<string> Names(string text, string pattern) =>
        Regex.Matches(text, pattern).Select(m => m.Groups[1].Value);

    /// <summary>逐个绑定表达式地给出（根名, 是否绑到模板父级/祖先）。范围取到该绑定的收尾大括号为止。</summary>
    private static IEnumerable<(string Path, bool TemplatedParentBound)> Bindings(string text)
    {
        foreach (Match m in Regex.Matches(text, BindPattern))
        {
            var end = text.IndexOf('}', m.Index);
            var scope = end < 0 ? text[m.Index..] : text[m.Index..end];
            yield return (m.Groups[1].Value, Regex.IsMatch(scope, TemplatedSourceFlag));
        }
    }

    /// <summary>
    /// "设置"入口全站只留一处（FR-48）。
    /// <para>两个入口一定会漂出两个行为：这次就是把设置提到菜单一级时，
    /// 顶部那个按钮如果留着，就会出现"点了按钮开的窗口和菜单开的不一样"。</para>
    /// </summary>
    [Fact]
    public void 设置入口全站只能有一处()
    {
        // 文案.xaml 是"定义处"，不是入口；数的是界面里挂了这个键的地方。
        var 命中 = Tagged(AppDir, "*.xaml")
            .Where(x => x.File != "文案.xaml")
            .SelectMany(x => System.Text.RegularExpressions.Regex
                .Matches(x.Text, "菜单_设置|按钮_设置")
                .Select(m => $"{x.File}:{m.Value}"))
            .ToList();

        Assert.Single(命中);
        Assert.EndsWith("菜单_设置", 命中[0]);
    }

    /// <summary>
    /// 反馈条四个级别都得有各自的底色键（FR-46）。
    /// 少一个键不会报错，只会让那一级悄悄用回提示级的底色——于是"错误"看起来不像错误。
    /// </summary>
    [Fact]
    public void 反馈条的每个级别都要换到自己的底色键()
    {
        var 主题 = File.ReadAllText(Path.Combine(AppDir, "Resources", "主题.xaml"));
        var i = 主题.IndexOf("<Style x:Key=\"反馈条\"", StringComparison.Ordinal);
        Assert.True(i >= 0, "样式字典里没有 反馈条");
        var 段 = 主题[i..主题.IndexOf("</Style>", i, StringComparison.Ordinal)];

        foreach (var 键 in new[] { "渐变_徽标底", "渐变_提醒底", "渐变_错误底", "笔刷_底上文字" })
            Assert.Contains(键, 段);
    }

    [Fact]
    public void 学生票与儿童票只许出现在那句口径说明里()
    {
        // FR-34 的验收①："界面上不存在任何标注为学生票/儿童票的价格数字"。
        // 列 39 在同一席别下确实有第二条价格，而它的业务含义没确认（Q-05 残余）——
        // 所以这两个词只允许待在需求定死的那句中性和说明里，一旦出现在别处就是有人拿猜测当数据。
        var offenders = Tagged(AppDir, "*.xaml").Concat(Tagged(AppDir, "*.cs"))
            .Where(f => f.Text.Contains("学生") || f.Text.Contains("儿童"))
            .Select(f => f.File)
            .ToList();

        Assert.Equal(["文案.xaml"], offenders);
        var sentence = KeyLine("票价_口径");
        Assert.Contains("学生票、儿童票按官方规则计算", sentence);
        Assert.Contains("以 12306 显示的价格为准", sentence);
    }

    private static string KeyLine(string key) =>
        Tagged(AppDir, "*.xaml")
            .SelectMany(f => Regex.Matches(f.Text, @"<sys:String x:Key=""" + key + @""">([^<]*)</sys:String>").Select(m => m.Groups[1].Value))
            .Single();

    private static HashSet<string> PublicPropertyNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(MainViewModel).Assembly.GetTypes().Concat(typeof(Core.Ports.SavedRoute).Assembly.GetTypes()))
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                names.Add(property.Name);
        return names;
    }

    private static IEnumerable<(string File, string Text)> Tagged(string dir, string pattern)
    {
        var skip = new[] { Path.Combine(dir, "obj"), Path.Combine(dir, "bin") };
        return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
            .Where(p => !skip.Any(s => p.StartsWith(s, StringComparison.Ordinal)))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)));
    }

    /// <summary>取 <c>{Binding X}</c> / <c>{Binding X.Y}</c> 的路径；只有修饰符没有路径的那种（<c>{Binding Mode=OneWay}</c>）跳过。</summary>
    private const string BindPattern =
        @"\{Binding\s+(?!(?:Mode|Path|RelativeSource|ElementName|UpdateSourceTrigger|FallbackValue|TargetNullValue|StringFormat)\s*=)([\w.]+)";

    /// <summary>
    /// 模板里 <c>RelativeSource=TemplatedParent / AncestorType</c> 的绑定指向的是<b>控件自身的依赖属性</b>
    /// （如 ComboBox.IsDropDownOpen、Expander.IsExpanded），不是视图模型属性——"根名必须在代码里存在"
    /// 这个前提对它不适用。豁免只针对这一类，且按**单个绑定表达式**判定，不按文件、不按行号：
    /// 放宽到整本字典就等于把 DataContext 绑定也一起免检了。
    /// </summary>
    private const string TemplatedSourceFlag = @"RelativeSource\s*=\s*\{RelativeSource\s+(?:TemplatedParent|AncestorType)";

    private const string DynamicPattern = @"DynamicResource\s+([\w\u4e00-\u9fff]+)";
    private const string GetPattern = @"\bGet\(""([\w\u4e00-\u9fff]+)""\)";
    private const string KeyPattern = @"x:Key=""([\w\u4e00-\u9fff]+)""";
}
