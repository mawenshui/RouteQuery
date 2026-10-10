using RouteQuery.Core.Model;

namespace RouteQuery.Core.Logic;

/// <summary>本地筛选。对应 FR-11，全部在内存完成，不产生任何网络请求。</summary>
public static class JourneyFilter
{
    public static IReadOnlyList<TrainJourney> Apply(
        IReadOnlyList<TrainJourney> journeys, QueryCriteria criteria)
    {
        if (!criteria.HasAnyFilter) return journeys;

        return journeys.Where(j => Passes(j, criteria)).ToList();
    }

    private static bool Passes(TrainJourney j, QueryCriteria c)
    {
        if (c.Kinds.Count > 0 && !c.Kinds.Contains(QueryCriteria.KindOf(j.TrainCode))) return false;
        if (c.Bands.Count > 0 && !c.Bands.Contains(QueryCriteria.BandOf(j.Departure))) return false;
        if (c.SameDayArrival && j.ArrivesNextDay) return false;

        // 刻意用 IsAvailable：候补与"未开售"都不算有票。把候补算进来会让用户筛出一堆买不到的车。
        if (c.OnlyAvailable && !j.HasAnyTicket) return false;

        if (c.RequiredSeat is { } seat)
        {
            var s = j.Seats.FirstOrDefault(x => x.Class == seat);
            if (s is null || !s.IsAvailable) return false;
        }

        return true;
    }
}

