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
    private static readonly Regex BrushUse = new(@"笔刷_[\w\u4e00-\u9fff]+");

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

    private static IEnumerable<string> MarkupFiles() =>
        Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static bool IsPalette(string file) => file == Light || file == Dark;
}
