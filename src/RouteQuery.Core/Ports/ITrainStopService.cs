using RouteQuery.Core.Model;

namespace RouteQuery.Core.Ports;

/// <summary>
/// 车次经停站时刻表查询。对应 FR-14（P0），同时是区间扩展 FR-26 的唯一站序来源。
/// </summary>
public interface ITrainStopService
{
    /// <summary>
    /// 取一趟车的全程经停站序。
    /// </summary>
    /// <param name="trainNo">车次内部标识，必须由查询结果透传。</param>
    /// <param name="fromTelecode">本次查询区间的出发站三字码。</param>
    /// <param name="toTelecode">本次查询区间的到达站三字码。</param>
    /// <param name="travelDate">乘车日期，格式 yyyy-MM-dd（官方对 yyyyMMdd 会失败）。</param>
    /// <exception cref="Core.Errors.QueryException">
    /// 官方未给出站序时抛 <c>DataUnavailable</c>。<b>禁止</b>用站点码表推断经停站来"保住功能"（NFR-18）。
    /// </exception>
    Task<TrainRoute> GetRouteAsync(
        string trainNo,
        string fromTelecode,
        string toTelecode,
        DateOnly travelDate,
        CancellationToken cancellationToken = default);
}
