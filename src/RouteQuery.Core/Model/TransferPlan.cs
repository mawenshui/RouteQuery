namespace RouteQuery.Core.Model;

/// <summary>
/// 中转方案的一段。对应 FR-06。
/// <para><b>它不是 <see cref="TrainJourney"/> 的复用</b>，因为官方在 <c>API-03</c> 的响应里
/// 给这两段的字段比余票接口少：没有 <c>canWebBuy</c>、没有候补标记、<b>也没有任何票价字段</b>
/// （2026-10-10 实测）。硬套成一个 TrainJourney 就等于凭空补上"可购状态"和"票价"两样
/// 官方没给的东西——那正是 NFR-18 禁止的形态。</para>
/// </summary>
/// <param name="Seats">席别余票。<see cref="SeatAvailability.Price"/> 一律为 null：官方不给。</param>
/// <param name="DayDifference">跨几天到达（官方 <c>day_difference</c>，字符串数字）。</param>
public sealed record TransferLeg(
    string TrainCode,
    string TrainNo,
    Station From,
    Station To,
    TimeOnly Departure,
    TimeOnly Arrival,
    TimeSpan Duration,
    int DayDifference,
    IReadOnlyList<SeatAvailability> Seats);

/// <summary>
/// 一个中转方案：出发 → 中转 → 到达 的两段拼接。对应 FR-06 / FR-15。
/// </summary>
/// <param name="SameStation">两段是否在同一车站换乘（官方 <c>same_station</c>）。不同站换乘要跨城，必须显式告知。</param>
/// <param name="RequiresExitingStation">是否需要出站再进站（官方 <c>isOutStation</c>）。</param>
/// <param name="WaitMinutes">中转候车分钟数（官方 <c>wait_time_minutes</c>）。负值方案在解析层就被丢弃。</param>
/// <param name="TotalDurationText">官方原文的总历时（如 <c>4小时18分钟</c>）。<b>它不是 <c>hh:mm</c></b>——
/// 实测这一段中文里带"小时/分钟"字样，按时间解析会得到 0。因此总历时以官方给的分钟数为准，
/// 原文只用于显示。</param>
/// <param name="PricesKnown">官方是否给了票价。<b>实测恒为 false</b>——留着这个字段是为了让界面
/// 有个明确的"票价未给出"可说，而不是让每个调用点各自猜。</param>
public sealed record TransferPlan(
    TransferLeg First,
    TransferLeg Second,
    Station MiddleStation,
    TimeSpan WaitTime,
    int WaitMinutes,
    TimeSpan TotalDuration,
    int TotalMinutes,
    string TotalDurationText,
    DateOnly TravelDate,
    DateOnly ArrivalDate,
    bool SameStation,
    bool RequiresExitingStation,
    bool PricesKnown)
{
    public bool ArrivesNextDay => ArrivalDate > TravelDate;

    /// <summary>任一段有票即算"这个方案眼下走得通"。两段都没票时不得显示成有票。</summary>
    public bool HasAnyTicket => First.Seats.Any(s => s.IsAvailable) && Second.Seats.Any(s => s.IsAvailable);

    /// <summary>到达时刻。跨零点时第二段的 arrive_time 是次日钟点，界面另用 ArrivesNextDay 标注。</summary>
    public string ArrivalTimeText() => $@"{Second.Arrival:hh\:mm}";
}
