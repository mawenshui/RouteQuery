using RouteQuery.Core.Logic;
using RouteQuery.Core.Model;

namespace RouteQuery.Tests;

/// <summary>
/// 本地筛选与排序（FR-11 / FR-12）。对应验收用例 TC-C1 ~ TC-C4。
/// <para>这两块是"不发请求却会改变用户决定"的代码：排错一位，亲友看到的第一趟车就不是同一趟；
/// 把缺价当成 0 元，最低价排序会把买不到的车推到最前面。它们又完全不依赖界面，
/// 是本项目里最该被测试钉死、也最容易钉死的部分。</para>
/// </summary>
public class FilterAndSortTests
{
    private static readonly Station Beijing = new("北京南", "VNP", "beijingnan", "bjn", "1001", "北京");
    private static readonly Station Shanghai = new("上海虹桥", "AOH", "shanghaihongqiao", "shq", "0201", "上海");

    private static TrainJourney J(string code, string dep, string arr, string dur,
        decimal? price = 500m, bool nextDay = false, SeatState state = SeatState.Available) => new(
        code, $"NO{code}", Beijing, Shanghai, Beijing, Shanghai,
        TimeSpan.Parse(dep), TimeSpan.Parse(arr), TimeSpan.Parse(dur),
        nextDay, PurchaseState.Buyable,
        [new SeatAvailability(SeatClass.SecondClass, state.ToString(), state, price)],
        null);

    private static readonly TrainJourney Early = J("G10", "06:00", "11:00", "05:00", price: 600m);
    private static readonly TrainJourney Midday = J("G20", "12:00", "16:30", "04:30", price: 500m);
    private static readonly TrainJourney Late = J("G30", "19:00", "23:30", "04:30", price: 550m);

    [Theory]
    [InlineData(SortKey.DepartureEarliest, "G10", "G20", "G30")]
    [InlineData(SortKey.DepartureLatest, "G30", "G20", "G10")]
    [InlineData(SortKey.ShortestDuration, "G20", "G30", "G10")]   // 同历时再按出发时刻
    [InlineData(SortKey.ArrivalEarliest, "G10", "G20", "G30")]
    [InlineData(SortKey.LowestPrice, "G20", "G30", "G10")]
    public void 五个排序键各自成立(SortKey key, string first, string second, string third) =>
        Assert.Equal([first, second, third],
            JourneySorter.Apply([Late, Early, Midday], key).Select(j => j.TrainCode));

    [Fact]
    public void 跨零点的夜车按到达排序不能被排成最早到达()
    {
        // 23:00 发、次日 01:00 到，钟点上比 06:00 发的车"更早"，时间轴上其实更晚（BF-05 同源）。
        var night = J("Z1", "23:00", "01:00", "02:00", nextDay: true);
        var morning = J("G1", "06:00", "10:00", "04:00");

        var sorted = JourneySorter.Apply([night, morning], SortKey.ArrivalEarliest).Select(j => j.TrainCode).ToList();

        Assert.Equal(["G1", "Z1"], sorted);
    }

    [Fact]
    public void 没有票价的车次排在有价之后而不是被当成零元()
    {
        // NFR-16：缺价当 0 会让"最低价"排序把买不到票的车推到第一，用户按它做决定就亏了。
        var noPrice = J("K1", "08:00", "20:00", "12:00", price: null);
        var priced = J("K2", "09:00", "21:00", "12:00", price: 120m);

        var sorted = JourneySorter.Apply([noPrice, priced], SortKey.LowestPrice).Select(j => j.TrainCode).ToList();

        Assert.Equal(["K2", "K1"], sorted);
    }

    [Fact]
    public void 同一份数据重复排序结果一致()
    {
        var data = new[] { Midday, Late, Early, J("G15", "06:00", "12:00", "06:00", price: 500m) };

        var first = JourneySorter.Apply(data, SortKey.LowestPrice).Select(j => j.TrainCode);
        var second = JourneySorter.Apply(data, SortKey.LowestPrice).Select(j => j.TrainCode);

        // 同价时必须有一个确定的次级键，否则顺序会随内部散列抖动，用户会以为结果被偷偷刷新过。
        Assert.Equal(first, second);
    }

    [Fact]
    public void 候补与未开售都不算有票()
    {
        var available = J("G1", "07:00", "11:00", "04:00");
        var waitlist = J("G2", "08:00", "12:00", "04:00", state: SeatState.Waitlist);
        var notOnSale = J("G3", "09:00", "13:00", "04:00", state: SeatState.NotYetOnSale);
        var criteria = new QueryCriteria { OnlyAvailable = true };

        var kept = JourneyFilter.Apply([available, waitlist, notOnSale], criteria).Select(j => j.TrainCode);

        Assert.Equal(["G1"], kept);
    }

    [Fact]
    public void 当天到达的筛选排除夜车()
    {
        var day = J("G1", "07:00", "11:00", "04:00");
        var night = J("Z1", "23:00", "01:00", "02:00", nextDay: true);

        var kept = JourneyFilter.Apply([day, night], new QueryCriteria { SameDayArrival = true })
            .Select(j => j.TrainCode);

        Assert.Equal(["G1"], kept);
    }

    [Fact]
    public void 车型与时段筛选按官方车次号首字母判断()
    {
        var gao = J("G1", "07:00", "11:00", "04:00");
        var pu = J("1461", "07:30", "20:00", "12:30");
        var evening = J("G9", "20:00", "23:00", "03:00");

        // 没有任何条件时三趟原样都在——"1461" 这种纯数字车次是普客，不是"未知车型"。
        Assert.Equal(["G1", "1461", "G9"], JourneyFilter.Apply([gao, pu, evening], new QueryCriteria())
            .Select(j => j.TrainCode));
        Assert.Equal(["G1", "G9"], JourneyFilter.Apply([gao, pu, evening],
            new QueryCriteria { Kinds = [TrainKind.GaoTie] }).Select(j => j.TrainCode));
        Assert.Equal(["G9"], JourneyFilter.Apply([gao, pu, evening],
            new QueryCriteria { Bands = [DepartureBand.Evening] }).Select(j => j.TrainCode));
    }

    [Fact]
    public void 没有任何筛选条件时原样返回不发二次处理()
    {
        var data = new[] { Early, Midday };

        Assert.Same(data, JourneyFilter.Apply(data, new QueryCriteria()));
    }
}
