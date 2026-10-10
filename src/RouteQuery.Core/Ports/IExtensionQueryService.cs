using RouteQuery.Core.Logic;
using RouteQuery.Core.Model;

namespace RouteQuery.Core.Ports;

/// <summary>一个候选区间的查询产出。</summary>
/// <param name="Candidate">候选区间。</param>
/// <param name="Journey">该车次在候选区间上的结果；官方没给出这趟车时为 null。</param>
/// <param name="NotSoldHere">true 表示候选区间里查不到这趟车（官方不卖这一程），不是错误。</param>
public sealed record ExtensionOutcome(
    ExtensionCandidate Candidate,
    TrainJourney? Journey,
    bool NotSoldHere = false);

/// <summary>整批扩展的进度，界面据此显示"已完成 n / m"与逐条插入结果。</summary>
public sealed record ExtensionProgress(int Done, int Total, ExtensionOutcome Outcome);

/// <summary>批次停止的原因。</summary>
public enum ExtensionStopReason
{
    /// <summary>正常跑完预算内的全部候选。</summary>
    Completed,

    /// <summary>用户点了"停止"。</summary>
    UserStopped,

    /// <summary>遇到风控或结构异常，<b>整批</b>中止而不是跳过继续（FR-27）。</summary>
    AbortedByUpstream,
}

/// <summary>一次扩展动作的结果集合。</summary>
public sealed record ExtensionBatch(
    IReadOnlyList<ExtensionOutcome> Outcomes,
    int CandidatesNotQueried,
    ExtensionStopReason StopReason,
    int RequestsIssued);

/// <summary>
/// 区间扩展（"多买几站"）。对应 FR-26 ~ FR-28。
/// <para>实现方<b>必须</b>满足三条：严格串行、相邻请求间隔 ≥ 3 秒、单批次请求数不超过硬上限 12
/// （SPEC-007 第四节）。这三条不是为了体验，而是这个功能被允许存在的条件。</para>
/// </summary>
public interface IExtensionQueryService
{
    Task<ExtensionBatch> RunAsync(
        TrainJourney target,
        DateOnly travelDate,
        int maxExtraStations,
        IProgress<ExtensionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
