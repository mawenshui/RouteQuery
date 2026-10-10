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
        $@"{plan.First.Departure:hh\:mm} 出发 · {plan.ArrivalTimeText()} 到达";

    public string TotalText { get; } = string.Format(text.Get("中转_全程"), plan.TotalDurationText);

    /// <summary>候车时长与是否需要出站。不同站换乘要单独标出来——那意味着要拖着行李换一座车站。</summary>
    public string TransferText { get; } =
        string.Format(text.Get("中转_换乘"), plan.WaitTime.TotalMinutes >= 60
            ? $@"{(int)plan.WaitTime.TotalHours}小时{plan.WaitTime.Minutes}分"
            : $@"{plan.WaitMinutes}分钟")
        + (plan.SameStation ? text.Get("中转_同站") : text.Get("中转_异站"))
        + (plan.RequiresExitingStation ? text.Get("中转_需出站") : string.Empty);

    public string FirstSeatsText { get; } = SeatTexts.Summary(plan.First.Seats, text);
    public string SecondSeatsText { get; } = SeatTexts.Summary(plan.Second.Seats, text);

    public string ArrivalDayNote { get; } = plan.ArrivesNextDay ? text.Get("中转_次日") : string.Empty;

    /// <summary>票价一栏：官方在这个接口里不给票价，就照实说。</summary>
    public string PriceText { get; } = plan.PricesKnown ? string.Empty : text.Get("中转_无票价");

    public bool HasTicket { get; } = plan.HasAnyTicket;
}
