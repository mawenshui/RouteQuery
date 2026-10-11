using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// 一个中转方案在界面上的一行。对应 FR-06 / FR-15。
/// <para>刻意<b>不显示票价</b>，而是明说"官方未给出"：实测这个响应里没有任何价格字段，
/// 留一个空白格会被读成"免费"或"0 元"，那是会让人做错决定的错（NFR-18）。</para>
/// </summary>
public sealed class TransferRowViewModel(TransferPlan plan, ITextProvider text)
{
    public TransferPlan Model { get; } = plan;

    public string TrainsText { get; } = $"{plan.First.TrainCode} → {plan.Second.TrainCode}";

    public string RouteText { get; } = $"{plan.First.From.Name} → {plan.MiddleStation.Name} → {plan.Second.To.Name}";

    public string TimesText { get; } =
        $@"{plan.First.Departure:HH\:mm} 出发 · {plan.ArrivalTimeText()} 到达";

    public string TotalText { get; } = string.Format(text.Get("中转_全程"), plan.TotalDurationText);

    /// <summary>
    /// 候车时长，<b>只此一项</b>。官方另有 same_station / isOutStation 两个 0/1 字段，
    /// 但 10 份样本里它们恒为 "0"、同时 same_train 却是 "Y"（同一趟车继续坐，显然不用换站）——
    /// 含义无法从这份样本确定，所以这里不写"需换车站/需出站"这种断言（NFR-18、DEC-16 同一口径）。
    /// </summary>
    public string TransferText { get; } = string.Format(text.Get("中转_换乘"),
        plan.WaitTime.TotalMinutes >= 60 ? $@"{(int)plan.WaitTime.TotalHours}小时{plan.WaitTime.Minutes}分" : $@"{plan.WaitMinutes}分钟");

    public string FirstSeatsText { get; } = SeatTexts.Summary(plan.First.Seats, text);
    public string SecondSeatsText { get; } = SeatTexts.Summary(plan.Second.Seats, text);

    /// <summary>跨日标注。钟点是次日 06:30 还是当天 06:30，对"要不要在车上睡一晚"是两个决定，
    /// 所以这一格不能省；跨两天以上也不能糊成"次日"。</summary>
    public string ArrivalDayNote { get; } = plan.ArrivalDayOffset switch
    {
        <= 0 => string.Empty,
        1 => text.Get("中转_次日"),
        var days => string.Format(text.Get("中转_多日后"), days),
    };

    /// <summary>票价一栏：官方在这个接口里不给票价，就照实说。</summary>
    public string PriceText { get; } = plan.PricesKnown ? string.Empty : text.Get("中转_无票价");

    public bool HasTicket { get; } = plan.HasAnyTicket;
}
