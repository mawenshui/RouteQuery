namespace RouteQuery.Core.Model;

/// <summary>
/// 一趟车在某个查询区间上的事实。对应 FR-05 / FR-09。
/// <para>字段含义与列位来自官方渲染脚本的具名字段赋值，见 SPEC-007 第二节。</para>
/// </summary>
/// <param name="TrainCode">车次号，如 G531 或 1461（普客为纯数字，自检正则必须允许）。</param>
/// <param name="TrainNo">车次内部标识，经停站查询与区间扩展都要用它，由结果透传，绝不要求用户输入。</param>
/// <param name="From">本次查询的出发站。</param>
/// <param name="To">本次查询的到达站。</param>
/// <param name="StartStation">该车次的始发站（可能比 From 更靠前，界面据此提示"这是全程车的一程"）。</param>
/// <param name="EndStation">该车次的终到站。</param>
/// <param name="Departure">发车时刻。</param>
/// <param name="Arrival">到达时刻。</param>
/// <param name="Duration">历时；跨零点时已含 24 小时，不由界面推断。</param>
/// <param name="ArrivesNextDay">到达日是否为次日。显式建模，禁止用"到达早于出发"自行推断（SPEC-004 六.5）。</param>
/// <param name="PurchaseState">该区间当前的可购状态。</param>
/// <param name="Seats">各席别余票与票价。</param>
/// <param name="OnSaleAt">放票时间；官方未给时为 null，界面不显示该行而不是猜测。</param>
public sealed record TrainJourney(
    string TrainCode,
    string TrainNo,
    Station From,
    Station To,
    Station StartStation,
    Station EndStation,
    TimeSpan Departure,
    TimeSpan Arrival,
    TimeSpan Duration,
    bool ArrivesNextDay,
    PurchaseState PurchaseState,
    IReadOnlyList<SeatAvailability> Seats,
    DateTimeOffset? OnSaleAt)
{
    /// <summary>该区间当前是否可以在网上购票。</summary>
    public bool CanBuyOnline => PurchaseState == PurchaseState.Buyable;

    /// <summary>是否存在至少一个真正有票的席别（候补与未开售都不算）。</summary>
    public bool HasAnyTicket => Seats.Any(s => s.IsAvailable);

    /// <summary>各席别中的最低票价；全部缺失时为 null，排序时排到末尾而不是当作 0（FR-12）。</summary>
    public decimal? LowestPrice =>
        Seats.Any(s => s.Price.HasValue)
            ? Seats.Where(s => s.Price.HasValue).Min(s => s.Price!.Value)
            : null;
}

/// <summary>
/// 车次的可购状态。判据是官方 <c>canWebBuy</c> 列（列 11），实测取值域为 Y / N / IS_TIME_NOT_BUY。
/// <para>刻意保留 <see cref="Unknown"/>：官方若新增取值，界面要能说"不知道"，
/// 而不是把它塞进"不可购"或触发整批解析失败（SPEC-007 v1.7 规则 6）。</para>
/// </summary>
public enum PurchaseState
{
    /// <summary>可网上购票（Y）。</summary>
    Buyable,

    /// <summary>不可网上购票（N）——区间扩展功能的触发信号（FR-26）。</summary>
    NotBuyable,

    /// <summary>该日期尚未开售（IS_TIME_NOT_BUY）。</summary>
    NotYetOnSale,

    /// <summary>官方返回了本项目未见过且无法解释的取值。</summary>
    Unknown,
}
