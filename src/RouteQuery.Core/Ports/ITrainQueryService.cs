using RouteQuery.Core.Model;

namespace RouteQuery.Core.Ports;

/// <summary>一次直达查询的请求条件。</summary>
/// <param name="From">出发站（必须由码表给出，携带三字码）。</param>
/// <param name="To">到达站。</param>
/// <param name="TravelDate">乘车日期。是否合法由 <see cref="Logic.PresaleWindow"/> 在发请求前判定（FR-04）。</param>
public sealed record DirectQueryRequest(Station From, Station To, DateOnly TravelDate);

/// <summary>
/// 直达余票查询。对应 FR-05。
/// </summary>
public interface ITrainQueryService
{
    /// <summary>
    /// 查询一个区间的直达车次。
    /// </summary>
    /// <exception cref="Core.Errors.QueryException">
    /// 网络不可用 / 超时 / 被拒绝 / 需要登录 / 解析失败。空结果是<b>正常返回空集合</b>，不抛异常（FR-10）。
    /// </exception>
    Task<IReadOnlyList<TrainJourney>> SearchAsync(
        DirectQueryRequest request,
        CancellationToken cancellationToken = default);
}
