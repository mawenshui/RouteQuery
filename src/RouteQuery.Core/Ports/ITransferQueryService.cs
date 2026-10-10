using RouteQuery.Core.Model;

namespace RouteQuery.Core.Ports;

/// <summary>一次中转查询的产出。两个"被丢弃"计数不是装饰：方案数变少必须能说清为什么。</summary>
public sealed record TransferQueryResult(
    IReadOnlyList<TransferPlan> Plans, int DroppedForNegativeWait, int DroppedForShape);

/// <summary>
/// 官方接续换乘查询。对应 FR-06，前提是 FR-24 的登录态。
/// <para>取不到官方方案时的正确行为是<b>报错并说明</b>，绝不本地拼一套"看起来像"的换乘
/// （NFR-18、SPEC-007 一节）。</para>
/// </summary>
public interface ITransferQueryService
{
    Task<TransferQueryResult> SearchAsync(
        Station from, Station to, DateOnly travelDate, CancellationToken cancellationToken = default);
}
