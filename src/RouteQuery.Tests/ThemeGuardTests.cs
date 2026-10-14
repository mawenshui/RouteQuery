using System.IO;
using System.Text.RegularExpressions;

namespace RouteQuery.Tests;

/// <summary>
/// 主题守卫（FR-35）。两条判据都来自"错了不会报错、只会看不见"这一类：
/// 硬编码色值在深色档位下残留成浅色文字，缺一个色板键则那段文字直接落回系统默认色——
/// 两者都不会抛异常，用户只会读到一块看不清的字。
/// </summary>
public class ThemeGuardTests
{
    private static readonly string AppDir = Path.Combine(Samples.RepoRoot, "src", "RouteQuery.App");
    private static readonly string Light = Path.Combine(AppDir, "Resources", "浅色.xaml");
    private static readonly string Dark = Path.Combine(AppDir, "Resources", "深色.xaml");

    private static readonly Regex Key = new(@"x:Key=""([\w\u4e00-\u9fff]+)""");
    // 渐变键一起查：皮肤改的是底色，缺一个 渐变_ 键和缺一个 笔刷_ 键是同一类故障——
    // 那块底会静默落回系统默认色，运行时不报错（2026-10-12 深色档已经栽过一次）。
    private static readonly Regex BrushUse = new(@"(?:笔刷|渐变)_[\w一-鿿]+");

    /// <summary>
    /// 界面里出现的每一种控件类型，都必须在样式字典里有一条隐式样式。
    /// <para>为什么要有这一条：FR-44 说"所有控件都要有好看的样式"，而这句话最容易的实现方式是
    /// 只改当时想到的那几个控件。漏掉的那个不会报错——它会带着系统默认外观落在皮肤上，
    /// 深色档下就是一块白底（K-10 当年就是这么来的）。</para>
    /// <para>它判的是"有没有样式"，不是"样式好不好看"——后者仍然只能靠眼睛。</para>
    /// </summary>
    [Fact]
    public void 界面用到的每个控件类型都要在样式字典里有隐式样式()
    {
        var styled = System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(Path.Combine(AppDir, "Resources", "主题.xaml")), @"<Style TargetType=""([A-Za-z]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in MarkupFiles())
        {
            var file = Path.GetFileName(path);
            if (IsPalette(path) || file == "主题.xaml") continue;
            foreach (var tag in ElementTags(File.ReadAllText(path)))
            {
                var type = ResolveControl(tag);
                if (type is null || NoStyleNeeded.Contains(type) || styled.Contains(type)) continue;
                missing.Add($"{Path.GetRelativePath(Samples.RepoRoot, path).Replace('\\', '/')}：{tag}");
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>不需要皮肤的结构件，逐个给理由——空名单会让这条守卫变成"以后再加个理由就行"的口子。</summary>
    private static readonly HashSet<string> NoStyleNeeded =
        new(StringComparer.Ordinal)
        {
            nameof(System.Windows.Controls.ScrollViewer),
            nameof(System.Windows.Controls.ItemsControl),
        };
    //  ScrollViewer：它自己没有任何可见像素，可见的部分是里面的滚动条与外层底色，两者都已换过。
    //  ItemsControl：同上，它只负责把条目排下去，每个条目长什么样由 ItemTemplate 决定，
    //                 而模板里的东西（文本、按钮）都在本条守卫的管辖范围内。

    private static IEnumerable<string> ElementTags(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"<([A-Za-z][A-Za-z0-9]*)(?=[\s>/])")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal);

    /// <summary>标签名 → WPF 控件类型名。不是 Control 的子类（Grid/Border/TextBlock 等）返回 null：
    /// 它们没有"外观样式"这回事，颜色靠继承来的 Foreground 与自己的 Background。</summary>
    private static string? ResolveControl(string tag)
    {
        foreach (var ns in new[] { "System.Windows.Controls", "System.Windows.Controls.Primitives" })
        {
            var t = Type.GetType($"{ns}.{tag}, PresentationFramework", throwOnError: false);
            if (t is not null && typeof(System.Windows.Controls.Control).IsAssignableFrom(t)) return t.Name;
        }
        return null;
    }

    [Fact]
    public void 界面文件里不许出现任何色值()
    {
        // FR-35 实现约束的原文："禁止在控件上硬编码 Foreground="#xxx""。
        // 具名色（White/Gray 之类）一起禁：毛病与写死色值相同，只是按 "#" grep 时会漏。
        // Transparent 不算色值（它不需要随主题变），所以不在名单里。
        var offenders = MarkupFiles()
            .Where(f => !IsPalette(f))
            .Where(f => HasColorLiteral(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(Samples.RepoRoot, f).Replace('\\', '/'))
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool HasColorLiteral(string text) =>
        Hex.Matches(text).Count > 0 || Named.Matches(text).Count > 0;

    private static readonly Regex Hex = new(@"#[0-9A-Fa-f]{3,8}\b");

    private static readonly Regex Named =
        new(@"=""(White|Black|Red|Green|Blue|Gray|Grey|Yellow|Orange|Silver|Maroon|Navy|Purple|Teal)""");

    [Fact]
    public void 两份色板的键必须一模一样()
    {
        // 切换主题是整本换字典：浅色有、深色没有的那个键，在深色档位下不会报错，
        // 只会让那段文字用系统默认色——深色底上一段黑字，正是验收①要拦的残留。
        var light = KeysOf(Light);
        var dark = KeysOf(Dark);

        Assert.Equal(light, dark);
        Assert.NotEmpty(light);
    }

    [Fact]
    public void 界面用到的每个色键都必须在两份色板里都有()
    {
        var required = MarkupFiles()
            .Where(f => !IsPalette(f))
            .SelectMany(f => BrushUse.Matches(File.ReadAllText(f)).Select(m => m.Value))
            .Distinct()
            .ToList();

        var light = KeysOf(Light);
        var dark = KeysOf(Dark);

        Assert.NotEmpty(required);   // 一个都不要求 = 这条测试什么都没看
        Assert.All(required, key => Assert.True(light.Contains(key) && dark.Contains(key),
            $"{key} 缺于{(light.Contains(key) ? "深色" : "浅色")}（写它的那处会在另一档里变成默认颜色）"));
    }

    private static HashSet<string> KeysOf(string file) =>
        Key.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    // ── 渐变皮肤（FR-38 / 设计 UI-001 v1.2 §六·五）─────────────────────
    //
    // 这一组判据是**先写出来、看着它红**再开始调颜色的。原因很直白：
    // "合适的对比度"如果只写在需求里，最后会变成"我看着顺眼"；
    // 而渐变最典型的翻车方式是"拿渐变中间那条颜色去算对比度"——两端早就跌破了。
    // 所以下面每一条都**对两端分别算**，任何一端不够就是不合格。

    private static readonly Regex Gradient = new(
        @"<LinearGradientBrush\s+x:Key=""([\w\u4e00-\u9fff]+)""([^>]*)>(.*?)</LinearGradientBrush>",
        RegexOptions.Singleline);

    private static readonly Regex Stop = new(@"Color=""(#[0-9A-Fa-f]{6,8})""");

    private static readonly Regex Point = new(@"(StartPoint|EndPoint)=""([\d.]+),([\d.]+)""");

    /// <summary>哪些底是"正文会落在上面"的——这些必须让正文与次级文字都过 AA。</summary>
    private static readonly (string Surface, string[] Texts)[] BodySurfaces =
    [
        ("渐变_窗口底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_内容底", ["笔刷_文字", "笔刷_次级文字", "笔刷_有票", "笔刷_成功", "笔刷_错误"]),
        ("渐变_行底", ["笔刷_文字", "笔刷_次级文字", "笔刷_有票", "笔刷_成功", "笔刷_错误"]),
        ("渐变_徽标底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_输入底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_弹层底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_页签底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_页签选中底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_提醒底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_按钮底", ["笔刷_文字", "笔刷_次级文字"]),
        ("渐变_按钮按下底", ["笔刷_文字"]),
        ("渐变_错误底", ["笔刷_底上文字"]),
    ];

    [Fact]
    public void 两档色板都必须有渐变画刷且每个至少两个色标()
    {
        foreach (var file in new[] { Light, Dark })
        {
            var name = Path.GetFileName(file);
            var gradients = Gradient.Matches(File.ReadAllText(file)).ToList();

            Assert.True(gradients.Count >= BodySurfaces.Length,
                $"{name} 只有 {gradients.Count} 个渐变画刷，皮肤要求覆盖的底色有 {BodySurfaces.Length} 种");

            Assert.All(gradients, g => Assert.True(Stop.Matches(g.Groups[3].Value).Count >= 2,
                $"{name} 的 {g.Groups[1].Value} 不足两个色标——那不是一个渐变，是一整块同色"));
        }
    }

    [Fact]
    public void 每个渐变必须是纵向同向()
    {
        // 设计 §六·五：同屏内所有渐变同向（纵向）。方向不一致时，一屏里会出现好几块"各自打光"的面板，
        // 那就是视觉噪声；斜向渐变在 125%/150% 这种分数缩放下还容易出带状断层。
        var bad = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { Light, Dark })
        {
            var found = Gradient.Matches(File.ReadAllText(file)).ToList();
            if (found.Count == 0)
            {
                // 一本没有渐变的色板让这条守卫"零违例通过"，那是假绿——必须点名。
                bad.Add($"{Path.GetFileName(file)} 里一个渐变画刷都没有，方向没法判");
                continue;
            }

            foreach (Match g in found)
            {
                var head = g.Groups[2].Value;
                var points = Point.Matches(head).ToDictionary(m => m.Groups[1].Value,
                    m => (X: double.Parse(m.Groups[2].Value), Y: double.Parse(m.Groups[3].Value)));
                if (!points.TryGetValue("StartPoint", out var s) || !points.TryGetValue("EndPoint", out var e))
                {
                    bad.Add($"{Path.GetFileName(file)}:{g.Groups[1].Value} 没写 StartPoint/EndPoint");
                    continue;
                }
                if (Math.Abs(s.X - e.X) > 0.001 || e.Y <= s.Y)
                    bad.Add($"{Path.GetFileName(file)}:{g.Groups[1].Value} 不是自上而下（{s.X},{s.Y}→{e.X},{e.Y}）");
            }
        }

        Assert.Empty(bad);
    }

    [Fact]
    public void 正文色对每个渐变的上下两端都要过AA()
    {
        var fails = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { Light, Dark })
        {
            var name = Path.GetFileName(file);
            var stops = GradientsByName(file);
            var solids = SolidsByName(file);

            foreach (var (surface, texts) in BodySurfaces)
            {
                if (!stops.TryGetValue(surface, out var ends))
                {
                    fails.Add($"{name} 缺底色键 {surface}");
                    continue;
                }

                foreach (var text in texts)
                {
                    if (!solids.TryGetValue(text, out var fg))
                    {
                        fails.Add($"{name} 缺文字键 {text}");
                        continue;
                    }

                    for (var i = 0; i < ends.Count; i++)
                    {
                        var ratio = Contrast(fg, ends[i]);
                        if (ratio < 4.5)
                            fails.Add($"{name}：{text} 落在 {surface} 的第 {i + 1} 端（{ends[i]}）只有 {ratio:0.00}:1，正文要求 ≥4.5:1");
                    }
                }
            }
        }

        Assert.Empty(fails);
    }

    [Fact]
    public void 可交互控件的边界要看得见()
    {
        // 判的是 笔刷_控件边界（输入框、按钮、下拉框的轮廓），不是 笔刷_边框（表格分隔线）。
        // 分隔线不承载信息，把它拉到 3:1 只会让满屏出现栅格线，反而更难看；
        // 而控件边界低于 3:1 就不是"淡雅"，是"看不出这里能填字"。
        var fails = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { Light, Dark })
        {
            var name = Path.GetFileName(file);
            var stops = GradientsByName(file);
            var solids = SolidsByName(file);
            // 键不存在时不许"跳过就算通过"：那会让守卫在皮肤被回退之后变成一条空转的检查。
            RequireKeys(file, stops, solids, fails);
            if (!solids.TryGetValue("笔刷_控件边界", out var border) || !stops.TryGetValue("渐变_内容底", out var surface))
                continue;

            foreach (var end in surface)
            {
                var ratio = Contrast(border, end);
                if (ratio < 3.0)
                    fails.Add($"{name}：笔刷_控件边界 对 渐变_内容底（{end}）只有 {ratio:0.00}:1，可交互边界要求 ≥3:1");
            }
        }

        Assert.Empty(fails);
    }

    [Fact]
    public void 数据区的行底渐变幅度必须压住()
    {
        // 行底渐变两端相对亮度差 ≤ 0.04（设计 §六·五）。
        // 幅度再大，一行里 40+ 个字段就会从"扫读"变成"逐字找"——这是本条唯一"宁可难看也要好读"的退让。
        var fails = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { Light, Dark })
        {
            var stops = GradientsByName(file);
            var solids = SolidsByName(file);
            RequireKeys(file, stops, solids, fails);
            if (!stops.TryGetValue("渐变_行底", out var ends)) continue;

            var spread = ends.Select(Luminance).Max() - ends.Select(Luminance).Min();
            if (spread > 0.04)
                fails.Add($"{Path.GetFileName(file)}：渐变_行底 两端亮度差 {spread:0.000}，数据区上限是 0.04");
        }

        Assert.Empty(fails);
    }

    private static Dictionary<string, List<string>> GradientsByName(string file)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Match g in Gradient.Matches(File.ReadAllText(file)))
            map[g.Groups[1].Value] = Stop.Matches(g.Groups[3].Value).Select(m => m.Groups[1].Value).ToList();
        return map;
    }

    private static Dictionary<string, string> SolidsByName(string file)
    {
        // 色板用的是**内容写法**：<SolidColorBrush x:Key="笔刷_文字">#1F1F1F</SolidColorBrush>。
        // 只认 Color= 属性写法会一个都匹配不上，于是对比度守卫拿着空字典"全绿"——假绿比没有守卫更糟。
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in new Regex(
                     @"<SolidColorBrush\s+x:Key=""([\w\u4e00-\u9fff]+)""\s*(?:Color=""(#[0-9A-Fa-f]{3,8})""|>(#[0-9A-Fa-f]{3,8})</SolidColorBrush>)").Matches(File.ReadAllText(file)))
            map[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        return map;
    }

    /// <summary>两档色板都得有这些键；缺键直接失败，不让守卫在回退之后变成空转。</summary>
    private static void RequireKeys(string file, Dictionary<string, List<string>> gradients,
        Dictionary<string, string> solids, SortedSet<string> fails)
    {
        var name = Path.GetFileName(file);
        foreach (var (surface, texts) in BodySurfaces)
            if (!gradients.ContainsKey(surface)) fails.Add($"{name}：缺底色键 {surface}");
        foreach (var key in new[] { "笔刷_文字", "笔刷_次级文字", "笔刷_底上文字",
                                    "笔刷_有票", "笔刷_成功", "笔刷_错误", "笔刷_控件边界" })
            if (!solids.ContainsKey(key)) fails.Add($"{name}：缺文字键 {key}");
    }

    /// <summary>WCAG 2.1 相对亮度。sRGB 先拆线性，再按 0.2126/0.7152/0.0722 加权。</summary>
    private static double Luminance(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length == 8) s = s[2..];                       // 带 AA 前缀的写法取后 6 位
        double Channel(int i)
        {
            var v = Convert.ToInt32(s.Substring(i * 2, 2), 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }

    private static double Contrast(string a, string b)
    {
        var (hi, lo) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static IEnumerable<string> MarkupFiles() =>
        Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static bool IsPalette(string file) => file == Light || file == Dark;
}
