using RouteQuery.Core.Logic;
using RouteQuery.Core.Model;

namespace RouteQuery.Tests;

/// <summary>预售期、区间扩展候选与多花金额三块纯逻辑。它们不触网，因此可以测到边界。</summary>
public class PureLogicTests
{
    // ── FR-03 预售期 ────────────────────────────────────────
    [Fact]
    public void 预售期为含当天十五天()
    {
        var today = new DateOnly(2026, 10, 10);
        var (min, max) = PresaleWindow.For(today);

        Assert.Equal(today, min);
        Assert.Equal(new DateOnly(2026, 10, 24), max);   // 实测边界：10-24 可查，10-25 起返回 HTML
    }

    [Fact]
    public void 越界日期在程序层被拒绝而不只是界面置灰()
    {
        var today = new DateOnly(2026, 10, 10);
        Assert.False(PresaleWindow.IsSelectable(today.AddDays(-1), today));
        Assert.False(PresaleWindow.IsSelectable(today.AddDays(15), today));
        Assert.True(PresaleWindow.IsSelectable(today.AddDays(14), today));
    }

    [Fact]
    public void 预售期配置异常时被钳制而不是照用()
    {
        var today = new DateOnly(2026, 10, 10);
        Assert.Equal(today, PresaleWindow.For(today, 0).Max);      // 非法值退化为只允许今天
        Assert.Equal(today, PresaleWindow.For(today, -5).Max);
        Assert.True(PresaleWindow.For(today, 999).Max <= today.AddDays(59));
    }

    // ── FR-26 扩展候选 ─────────────────────────────────────
    private static List<StopDetail> Route(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new StopDetail($"{i + 1:D2}", $"站{i}",
                i == 0 ? null : new TimeSpan(6 + i, 0, 0),
                i == count - 1 ? null : new TimeSpan(6 + i, 10, 0),
                null, i == 0, i == count - 1))
            .ToList();

    [Fact]
    public void 候选区间必须真包含目标区间()
    {
        var stops = Route(10);
        var cands = ExtensionCandidateGenerator.Generate(stops, 3, 6, 3);

        Assert.NotEmpty(cands);
        Assert.All(cands, c =>
        {
            Assert.True(c.BoardStation.StationNo.CompareTo(stops[3].StationNo) <= 0);
            Assert.True(c.AlightStation.StationNo.CompareTo(stops[6].StationNo) >= 0);
            Assert.True(c.ExtraStations > 0);
        });
    }

    [Fact]
    public void 少买几站的候选排在前面()
    {
        var cands = ExtensionCandidateGenerator.Generate(Route(20), 5, 10, 4);
        Assert.Equal(1, cands.Min(c => c.ExtraStations));
        Assert.Equal(cands.OrderBy(c => c.ExtraStations).Select(c => c.ExtraStations), cands.Select(c => c.ExtraStations));
    }

    [Fact]
    public void 两端同时扩展不超过上限()
    {
        var cands = ExtensionCandidateGenerator.Generate(Route(30), 10, 18, 5);
        Assert.All(cands, c => Assert.True(c.ExtraStations <= 5));
    }

    [Fact]
    public void 目标站为始发站时不产生前移候选()
    {
        var cands = ExtensionCandidateGenerator.Generate(Route(10), 0, 5, 3);
        Assert.All(cands, c => Assert.Equal(0, c.BoardAhead));
    }

    [Fact]
    public void 目标站为终到站时不产生后延候选()
    {
        var stops = Route(10);
        var cands = ExtensionCandidateGenerator.Generate(stops, 4, 9, 3);
        Assert.All(cands, c => Assert.Equal(0, c.RideBeyond));
    }

    [Fact]
    public void 全程票没有候选且不该发起任何请求()
    {
        Assert.Empty(ExtensionCandidateGenerator.Generate(Route(8), 0, 7, 5));
    }

    [Fact]
    public void 站序异常时返回空而不是造候选()
    {
        var stops = Route(8);
        Assert.Empty(ExtensionCandidateGenerator.Generate(stops, 5, 2, 3));   // 出发在到达之后
        Assert.Empty(ExtensionCandidateGenerator.Generate(stops, -1, 3, 3));
        Assert.Empty(ExtensionCandidateGenerator.Generate([], 0, 1, 3));
    }

    [Fact]
    public void 上限被硬钳制在十站()
    {
        // FR-29：K 大于 10 不是"配置更激进"，而是非法输入，必须钳制。
        var cands = ExtensionCandidateGenerator.Generate(Route(60), 30, 32, 999);
        Assert.All(cands, c => Assert.True(c.ExtraStations <= ExtensionCandidateGenerator.MaxExtraStations));
    }

    // ── FR-28 多花金额 ─────────────────────────────────────
    [Fact]
    public void 同席别对齐后计算差额()
    {
        var target = Journey(SeatClass.SecondClass, 49.00m);
        var cand = Journey(SeatClass.SecondClass, 58.50m);
        var c = new ExtensionCandidate(1, 0, Stop("01"), Stop("03"));

        var r = PriceDeltaCalculator.Compute(c, target, cand, SeatClass.SecondClass);
        Assert.Equal(9.50m, r.Delta);
        Assert.False(r.IsAnomalous);
    }

    [Fact]
    public void 任一侧缺价则报无法计算而不是零()
    {
        var target = Journey(SeatClass.SecondClass, 49.00m);
        var cand = Journey(SeatClass.SecondClass, null);
        var c = new ExtensionCandidate(1, 0, Stop("01"), Stop("03"));

        var r = PriceDeltaCalculator.Compute(c, target, cand, SeatClass.SecondClass);
        Assert.Null(r.Delta);
        Assert.False(r.IsAnomalous);
    }

    [Fact]
    public void 候选不存在该席别时判为无价()
    {
        var target = Journey(SeatClass.BusinessClass, 174.00m);
        var cand = Journey(SeatClass.SecondClass, 49.00m);   // 候选只有二等座，没有商务座
        var c = new ExtensionCandidate(0, 1, Stop("01"), Stop("03"));

        var r = PriceDeltaCalculator.Compute(c, target, cand, SeatClass.BusinessClass);
        Assert.False(PriceDeltaCalculator.SeatExistsIn(cand, SeatClass.BusinessClass));
        Assert.Null(r.Delta);
    }

    [Fact]
    public void 负差额判为异常而非正常数据()
    {
        // 更长区间票价理论上不低于短区间。出现负值意味着列位或席别对齐错了，
        // 照实显示会让用户按一个错数字决定多付钱（NFR-16）。
        var target = Journey(SeatClass.SecondClass, 49.00m);
        var cand = Journey(SeatClass.SecondClass, 40.00m);
        var c = new ExtensionCandidate(1, 0, Stop("01"), Stop("03"));

        var r = PriceDeltaCalculator.Compute(c, target, cand, SeatClass.SecondClass);
        Assert.True(r.IsAnomalous);
        Assert.Equal(-9.00m, r.Delta);
    }

    private static StopDetail Stop(string no) => new(no, "某站", null, null, null, false, false);

    private static TrainJourney Journey(SeatClass seat, decimal? price)
    {
        var st = new Station("保定", "BDP", "baoding", "bd", "0326", "保定");
        var availability = price is null
            ? Array.Empty<SeatAvailability>()
            : [new SeatAvailability(seat, "有", SeatState.Available, price)];
        return new TrainJourney("G1", "NO1", st, st, st, st,
            new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(1, 0, 0),
            false, PurchaseState.Buyable, availability, null);
    }
}
