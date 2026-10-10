using RouteQuery.Core.Model;

namespace RouteQuery.Core.Logic;

/// <summary>一个候选区间的费用结果。</summary>
/// <param name="Candidate">对应的候选区间。</param>
/// <param name="TargetFare">目标区间同席别票价。</param>
/// <param name="CandidateFare">候选区间同席别票价。</param>
/// <param name="Delta">多花的钱；无法确定时为 null。</param>
/// <param name="IsAnomalous">true 表示算出了负值——这几乎必然是解析错位，界面不得当正常数据渲染。</param>
public sealed record PriceDeltaResult(
    ExtensionCandidate Candidate,
    decimal? TargetFare,
    decimal? CandidateFare,
    decimal? Delta,
    bool IsAnomalous = false);

/// <summary>
/// "多买几站"的多花金额计算。对应 FR-28 / NFR-16。
/// <para>三条不容妥协的规则：</para>
/// <list type="number">
/// <item><description>两侧<b>必须</b>取同一席别。候选区间不售该席别时显示"该席别在此区间不售"，
/// 绝不拿邻近席别的价格相减。</description></item>
/// <item><description>任一侧票价缺失 → 结果为"无法计算"，<b>不得</b>落成 0。
/// 把缺失算成 0 等于告诉用户"不用多花钱"，那是会造成真实金钱损失的错。</description></item>
/// <item><description>算出<b>负值</b>即判为异常并上报，而不是显示出来。更长区间的票价理论上不低于短区间，
/// 出现负值说明列位或席别对齐错了——宁可不显示也不能让用户照一个错数字决定多付钱。</description></item>
/// </list>
/// </summary>
public static class PriceDeltaCalculator
{
    public static PriceDeltaResult Compute(
        ExtensionCandidate candidate,
        TrainJourney target,
        TrainJourney? candidateJourney,
        SeatClass seat)
    {
        var targetSeat = Find(target, seat);
        var targetFare = targetSeat?.Price;

        var candidateSeat = candidateJourney is null ? null : Find(candidateJourney, seat);
        var candidateFare = candidateSeat?.Price;

        if (targetFare is null || candidateFare is null)
            return new PriceDeltaResult(candidate, targetFare, candidateFare, null);

        var delta = candidateFare.Value - targetFare.Value;
        if (delta < 0)
            return new PriceDeltaResult(candidate, targetFare, candidateFare, delta, IsAnomalous: true);

        return new PriceDeltaResult(candidate, targetFare, candidateFare, delta);
    }

    /// <summary>该候选是否存在这个席别（存在但无价 = 售而不报价，属"无法计算"）。</summary>
    public static bool SeatExistsIn(TrainJourney journey, SeatClass seat) => Find(journey, seat) is not null;

    private static SeatAvailability? Find(TrainJourney journey, SeatClass seat) =>
        journey.Seats.FirstOrDefault(s => s.Class == seat);
}
