using RouteQuery.Core.Model;

namespace RouteQuery.Core.Logic;

/// <summary>排序键。界面显示为"最早出发 / 最晚出发 / 历时最短 / 到达最早 / 最低价"。</summary>
public enum SortKey { DepartureEarliest, DepartureLatest, ShortestDuration, ArrivalEarliest, LowestPrice }

/// <summary>本地排序。对应 FR-12。</summary>
public static class JourneySorter
{
    /// <summary>
    /// 排序。<b>同值时一律以出发时刻作次级键</b>，保证同一份数据每次排出来顺序一样——
    /// 顺序不稳定会让人怀疑结果被偷偷刷新过。
    /// </summary>
    public static IReadOnlyList<TrainJourney> Apply(
        IReadOnlyList<TrainJourney> journeys, SortKey key)
    {
        var ordered = key switch
        {
            // 缺价的车次排到末尾而不是当作 0 元（已售罄时官方可能不返回价格）。
            SortKey.LowestPrice => journeys
                .OrderBy(j => j.LowestPrice is null ? 1 : 0)
                .ThenBy(j => j.LowestPrice ?? 0m),

            SortKey.ShortestDuration => journeys
                .OrderBy(j => j.Duration)
                .ThenBy(j => j.Departure),

            SortKey.DepartureLatest => journeys
                .OrderByDescending(j => j.Departure)
                .ThenBy(j => j.TrainCode, StringComparer.Ordinal),

            SortKey.ArrivalEarliest => journeys
                .OrderBy(j => ArrivalOnTimeline(j))
                .ThenBy(j => j.Departure),

            _ => journeys.OrderBy(j => j.Departure).ThenBy(j => j.TrainCode, StringComparer.Ordinal),
        };

        return ordered.ToList();
    }

    /// <summary>
    /// 到达时间在"发车日 0 点为原点"的时间轴上的位置。
    /// 跨零点的夜车必须 +24 小时后再比较，否则"前一天 23 发到次日 01 到"会被排成最早到达。
    /// </summary>
    private static TimeSpan ArrivalOnTimeline(TrainJourney j) =>
        j.Arrival + (j.ArrivesNextDay ? TimeSpan.FromDays(1) : TimeSpan.Zero);
}
