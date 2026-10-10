namespace RouteQuery.Core.Model;

/// <summary>
/// 一趟车经停站序上的一个站。对应 FR-14，数据来自 <c>API-04 = czxx/queryByTrainNo</c>。
/// <para>该接口返回的是<b>具名字段数组</b>，不像 <c>API-02</c> 那样依赖列位，因此本类型不需要列布局表。</para>
/// </summary>
/// <param name="StationNo">站序号（官方 <c>station_no</c>，如 "07"）。<b>必须</b>以它为站序依据，
/// 不得用数组下标假定无跳号（FR-26 的 i/j 由此得出）。</param>
/// <param name="StationName">站名。</param>
/// <param name="ArriveTime">到达时刻；始发站官方给的是占位符（如 "----"），此时为 null。</param>
/// <param name="DepartureTime">发车时刻；终到站为 null。</param>
/// <param name="Stopover">停留时长文本（如 "13分钟"），原样保留官方口径。</param>
/// <param name="IsFirst">是否为该车次始发站。</param>
/// <param name="IsLast">是否为该车次终到站。</param>
public sealed record StopDetail(
    string StationNo,
    string StationName,
    TimeSpan? ArriveTime,
    TimeSpan? DepartureTime,
    string? Stopover,
    bool IsFirst,
    bool IsLast);

/// <summary>一趟车的全程经停站序。区间扩展的候选全部由它生成（FR-26）。</summary>
/// <param name="TrainNo">车次内部标识。</param>
/// <param name="DisplayTrainCode">官方在该站序上显示的车次号。
/// <b>注意</b>：上下行同一条线路可能显示不同车次号（实测 K599 的经停表显示 K598），
/// 界面必须按官方返回值显示，不做归一化。</param>
/// <param name="Stops">按站序排列的停靠站。为空视为"官方未给出数据"，判 <c>DataUnavailable</c>（NFR-18）。</param>
public sealed record TrainRoute(
    string TrainNo,
    string DisplayTrainCode,
    IReadOnlyList<StopDetail> Stops);
