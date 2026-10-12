using System.IO;
using System.Text.RegularExpressions;

namespace RouteQuery.Tests;

/// <summary>
/// 合规硬检查（SPEC-004 第八节自查清单 + SPEC-006 第四节发布前清单里的"凭据自查"）。
/// <para>为什么写成测试而不是"发布前 grep 一次"：手工检查只在**有人真的做了那天**有效。
/// 本项目已经有过两次"规范里写着、实现里没做"——色板骨架（v1.4 承诺，第 5 轮发现根本没落地）
/// 和随包放 <c>stations.json</c>（清单里没有的形态，成了亲友的故障源）。
/// 能机检的约束不落成测试，就等于把遵守留给了记性。</para>
/// <para>扫描范围只有会被发布的三个工程（App / Core / Data），不含测试工程：
/// 测试里必然要写"违例长什么样"，把它算进来会让守卫自己把自己拦下。</para>
/// </summary>
public class ComplianceGuardTests
{
    private static readonly string SrcRoot = Path.Combine(Samples.RepoRoot, "src");

    private static readonly string[] Shipped = ["RouteQuery.App", "RouteQuery.Core", "RouteQuery.Data"];

    /// <summary>允许出现"按位置取下标"的布局表文件——SPEC-004 一：全库唯一允许裸下标的地方。</summary>
    private static readonly string[] LayoutFiles =
        ["LeftTicketColumnLayout.cs", "StationCodeTableLayout.cs", "TransferFieldMap.cs", "ColumnSpec.cs"];

    [Fact]
    public void 界面层与业务层都不许自己发网络请求()
    {
        // 官方流量必须全部收敛在数据层，否则"一次调用 = 一次请求"的计数与闸门预算就不成立。
        // 检查 HttpClient 而不是检查 URL 字符串：新造一个连接比写一个字面量更容易绕过 grep。
        var offenders = Sources("RouteQuery.App", "RouteQuery.Core")
            .Where(f => Code(f).Contains("HttpClient", StringComparison.Ordinal))
            .Select(Rel)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void 界面层与业务层都不许解析响应或按位置取列()
    {
        // SPEC-004 第八节原文："无 URL / 字段下标 / JSON 解析出现在 App 或 Core 层"。
        // 一旦解析漏到界面层，官方改列序时就没人知道该去哪儿改——错位的数据会一路显示到亲友眼前。
        var offenders = Sources("RouteQuery.App", "RouteQuery.Core")
            .Select(f => (File: f, Code: Code(f)))
            .Where(x => Regex.IsMatch(x.Code, @"\bJsonDocument\b|\bJsonSerializer\b|System\.Text\.Json|Split\('\|'\)|\b(parts|cols|columns|record|fields)\s*\[\s*\d")
                && !(x.File.EndsWith("Composition.cs", StringComparison.Ordinal)   // 组合根只负责装配，见下条测试
                     || x.File.EndsWith("App.xaml.cs", StringComparison.Ordinal)))
            .Select(x => Rel(x.File))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void 数据层里按位置取下标只许出现在布局表()
    {
        var offenders = Sources("RouteQuery.Data")
            .Select(f => (File: f, Code: Code(f)))
            .Where(x => !LayoutFiles.Any(l => x.File.EndsWith(l, StringComparison.Ordinal)))
            .Where(x => Regex.IsMatch(x.Code, @"\b(parts|cols|columns|record|fields)\s*\[\s*\d"))
            .Select(x => Rel(x.File))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void 全库不存在任何能接收密码或验证码的入参与字段()
    {
        // AGENTS 五.2 的硬约束：凭据只由用户本人在官方页面输入。
        // 这条守的是"代码形态"——只要存在这样一个参数、字段或控件，
        // 不管有没有人调用，它都是一个会被误用的入口。
        var forbidden = new[]
        {
            @"password", @"passwd", @"pwd", @"captcha", @"verifycode", @"verify_code", @"smscode", @"yzm", @"authcode",
        };

        var offenders = Sources(Shipped)
            .Select(f => (File: f, Code: Code(f)))
            .Where(x => forbidden.Any(p => Regex.IsMatch(x.Code, @"[\w一-鿿]*" + p + @"[\w一-鿿]*", RegexOptions.IgnoreCase))
                     || x.Code.Contains("PasswordBox", StringComparison.Ordinal))
            .Select(x => Rel(x.File))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void 没有调试输出残留()
    {
        // 桌面程序没有控制台：Console.WriteLine 要么是忘了删的调试残留，
        // 要么是想绕开统一日志偷偷写东西——两种都不该进发布件。
        var offenders = Sources(Shipped)
            .Where(f => Code(f).Contains("Console.Write", StringComparison.Ordinal)
                     || Code(f).Contains("Debug.WriteLine", StringComparison.Ordinal))
            .Select(Rel)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void 守卫本身不是空转的()
    {
        // 一条永远通过的守卫比没有守卫更糟：它给人一种"已经检查过了"的错觉。
        // 这里断言每条检查确实看到了足够多的源码，且违例形状能被识别。
        var files = Sources(Shipped).ToList();
        Assert.True(files.Count > 30, $"只扫到 {files.Count} 个源文件，扫描路径大概变了");

        // 故意喂给它一个违例形状：识别不出来就说明正则写坏了。
        Assert.Matches(@"\b(parts|cols|columns|record|fields)\s*\[\s*\d", "var x = parts[39];");
        Assert.Matches(new Regex(@"[\w一-鿿]*password[\w一-鿿]*", RegexOptions.IgnoreCase), "string userInputPassword");
    }

    /// <summary>读源码并**去掉注释与字符串字面量**再交给检查。</summary>
    private static string Code(string file) => Strip(File.ReadAllText(file));

    private static string Strip(string text) =>
        CommentOrString.Replace(text, m => m.Value.StartsWith('/') ? " " : " \"\" ");

    /// <summary>行注释、块注释、普通字符串、逐字字符串、内插字符串——一次性替换成占位。</summary>
    private static readonly Regex CommentOrString = new(
        @"//[^\n]*|/\*.*?\*/|@"">""(?:\d""|[^""])*""|""(?:\\""[\s\S]|[^\\""\n])*""",
        RegexOptions.Singleline);

    private static IEnumerable<string> Sources(params string[] projects) =>
        Sources((IEnumerable<string>)projects);

    private static IEnumerable<string> Sources(IEnumerable<string> projects) =>
        projects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(SrcRoot, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Concat(Directory.EnumerateFiles(Path.Combine(SrcRoot, "RouteQuery.App"), "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")));

    private static string Rel(string file) => Path.GetRelativePath(Samples.RepoRoot, file).Replace('\\', '/');
}
