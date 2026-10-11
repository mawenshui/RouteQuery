using System.IO;
using RouteQuery.App.ViewModels;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.Tests;

/// <summary>
/// 起售时间的界面口径（FR-33）。这一条的数据早就在余票响应的列 55 里，
/// 要测的只有三件事：官方没给时不许出现、没到点时给出时刻、到点后不许再写成"某日起售"。
/// </summary>
public class OnSaleDisplayTests
{
    private static readonly DateTimeOffset QueryTime = new(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(8));

    private static JourneyRowViewModel Row(DateTimeOffset? onSaleAt) =>
        new(Journey(onSaleAt), new FormatAsNotedText(), QueryTime);

    [Fact]
    public void 官方没给放票时间时这一行整体不出现()
    {
        var row = Row(null);

        Assert.False(row.HasOnSale);
        Assert.Equal(string.Empty, row.OnSaleNote);
    }

    [Fact]
    public void 还没到起售时刻要写明是几点的车()
    {
        var row = Row(new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.FromHours(8)));

        Assert.True(row.HasOnSale);
        Assert.Equal("10月28日 08:00 起售", row.OnSaleNote);
    }

    [Fact]
    public void 已过起售时刻的车不许再显示成将来某天起售()
    {
        // 对一趟今天已经在卖的车写"9 月 28 日起售"，会被读成"还没开卖"——
        // 这一句是 FR-33 明确要求分开的两种话。
        var row = Row(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.FromHours(8)));

        Assert.True(row.HasOnSale);
        Assert.Equal("已过起售时间", row.OnSaleNote);
    }

    [Fact]
    public void 起售判断只比较本次查询的时刻()
    {
        // 差一分钟也算"还没起售"，不许取整成"12:00"——那是把官方给的时刻改掉后显示给用户。
        Assert.Equal("10月11日 12:01 起售", Row(QueryTime + TimeSpan.FromMinutes(1)).OnSaleNote);
    }

    [Fact]
    public void 到点提醒所需的定时器一个都不许有()
    {
        // FR-33 把"到点提醒"留在 V1 之外，理由不是体验而是合规：应用不得自动或后台发请求（SPEC-007 四）。
        // 界面显示的是查询那一刻的快照，所以这条守的是**会被发布的三个工程**里根本不出现计时器
        // （测试工程自己的文件名里带这个词不算，扫它只会让这个守卫没法读）。
        var src = Path.Combine(Samples.RepoRoot, "src");
        var offenders = new[] { "RouteQuery.App", "RouteQuery.Core", "RouteQuery.Data" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(src, project), "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".xaml", StringComparison.Ordinal))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            .Where(f => File.ReadAllText(f).Contains("Timer", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Samples.RepoRoot, f))
            .ToList();

        Assert.Empty(offenders);
    }

    private static TrainJourney Journey(DateTimeOffset? onSaleAt)
    {
        var st = new Station("北京南", "VNP", "beijingnan", "bjj", "1001", "北京");
        return new TrainJourney("G1", "240000G10L", st, st, st, st,
            new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(1, 0, 0),
            false, PurchaseState.Buyable,
            [new SeatAvailability(SeatClass.SecondClass, "有", SeatState.Available, 553.00m)],
            onSaleAt);
    }

    /// <summary>用界面真文案回话：这样断言的是"用户看到的那句话"，不只是"用了哪个键"。</summary>
    private sealed class FormatAsNotedText : ITextProvider
    {
        public string Get(string key) => key switch
        {
            "详情_即将起售" => "{0} 起售",
            "详情_已过起售" => "已过起售时间",
            _ => key,
        };
    }
}
