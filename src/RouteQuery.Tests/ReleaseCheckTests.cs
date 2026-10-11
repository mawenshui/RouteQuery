using System.IO;
using RouteQuery.App.Update;
using RouteQuery.Data.Http;
using RouteQuery.Data.Services;

namespace RouteQuery.Tests;

/// <summary>
/// 检查更新（FR-36 / SPEC-007 六.1 唯一允许的第三方出站）。
/// 这里测的重点不是"能不能取到版本号"，而是<b>它不许越界</b>：比错版本会反复骚扰用户，
/// 共享客户端会把官方会话带出去，失败出声会把"没网"变成"应用坏了"。
/// </summary>
public class ReleaseCheckTests
{
    [Theory]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("v0.3.1", "0.3.0", true)]
    [InlineData("0.10.0", "0.9.0", true)]            // 按数字比：按字符串比会得到 0.10 < 0.9
    [InlineData("0.2.0", "0.2.0-rc2", true)]          // rc 转正式版是真升级
    [InlineData("0.2.0-rc2", "0.2.0", false)]         // 反过来不是
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.2.0-rc1", "0.2.0-rc2", false)]     // 后缀之间没有可靠的顺序，宁可不提示
    [InlineData("0.2", "0.2.0", false)]
    [InlineData(null, "0.2.0", false)]
    [InlineData("", "0.2.0", false)]
    [InlineData("最新版本", "0.2.0", false)]
    public void 版本比较只在确有更新时给出提示(string? latest, string current, bool expected)
        => Assert.Equal(expected, ReleaseChecker.IsNewer(latest, current));

    [Fact]
    public async Task 取不到版本时静默返回空而不把失败说给用户()
    {
        // 取消的令牌走的是同一条"任何失败都不出声"的路径：不抛、不返回半成品。
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Null(await new ReleaseFeedClient().FetchLatestVersionAsync(cts.Token));
    }

    [Fact]
    public void 版本检查客户端不能被递进任何官方依赖()
    {
        // DEC-15 的隔离是靠"它拿不到官方那一套东西"来保证的：
        // 只能有一个无参构造 = 拿不到 RequestGate，也拿不到 OfficialClient 的 CookieContainer。
        // 哪天有人给它加一个官方客户端参数，这条测试先响。
        Assert.All(typeof(ReleaseFeedClient).GetConstructors(), ctor => Assert.Empty(ctor.GetParameters()));
    }

    [Fact]
    public void 老设置文件里没有这一项时仍然默认开启()
    {
        // FR-36 写的是"默认开启，可在设置页关闭"。亲友机器上的 settings.json 是上一版写的，
        // 里面没有这个字段——反序列化必须落到默认值 true，而不是落到 false 或抛异常。
        var path = Path.Combine(Path.GetTempPath(), $"rq-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"MaxExtensionStations":5,"DisclaimerAccepted":true}""");
            var store = new SettingsStore(path);

            Assert.True(store.Load().CheckUpdatesEnabled);

            store.Save(store.Load() with { CheckUpdatesEnabled = false });
            Assert.False(new SettingsStore(path).Load().CheckUpdatesEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 界面层与业务层都不许出现地址字面量()
    {
        // AGENTS 六.1：URL 只许写在数据层。SPEC-007 六.1：例外的那个目标也必须写死。
        // 两条都靠"全项目只有这一处地址"才成立，地址散到界面层就没法审了。
        // XAML 的 xmlns 是框架命名空间不是地址，必须排除——否则每个文件都误报，检查会被当噪音跳过。
        var src = Path.Combine(Samples.RepoRoot, "src");
        var offenders = new[] { "RouteQuery.App", "RouteQuery.Core" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(src, project), "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Where(f => LinesWithAddress(File.ReadAllLines(f)).Any()))
            .Select(f => Path.GetRelativePath(Samples.RepoRoot, f).Replace('\\', '/'))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> LinesWithAddress(IEnumerable<string> lines) =>
        lines.Where(l => l.Contains("http://", StringComparison.Ordinal) || l.Contains("https://", StringComparison.Ordinal))
            .Where(l => !l.Contains("xmlns", StringComparison.Ordinal));
}
