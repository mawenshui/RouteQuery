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
            foreach (var path in Names(text, BindPattern))
            {
                // 只查根名：Selected.Train 的根是 MainViewModel.Selected，Train 由它自己那一层解析。
                var root = path.Split('.')[0];
                if (!known.Contains(root)) missing.Add($"{file}:{root}");
            }

        Assert.Empty(missing);
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

    private const string DynamicPattern = @"DynamicResource\s+([\w\u4e00-\u9fff]+)";
    private const string GetPattern = @"\bGet\(""([\w\u4e00-\u9fff]+)""\)";
    private const string KeyPattern = @"x:Key=""([\w\u4e00-\u9fff]+)""";
}
