using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// 结果列表里的一行。对应 FR-09。
/// <para>刻意<b>不做</b>的事：不重新计算任何业务数值。历时、次日、余票状态都在数据层算好，
/// 这里只负责"怎么显示"——否则同一个数字会有两套算法，迟早对不上。</para>
/// </summary>
public sealed class JourneyRowViewModel(TrainJourney journey, ITextProvider text) : Mvvm.ViewModelBase
{
    public TrainJourney Model { get; } = journey;

    public string TrainCode => Model.TrainCode;
    public string FromName => Model.From.Name;
    public string ToName => Model.To.Name;
    public string DepartureText => Model.Departure.ToString(@"hh\:mm");
    public string ArrivalText => Model.Arrival.ToString(@"hh\:mm");
    public string DurationText => Model.Duration.ToString(@"hh\:mm");

    /// <summary>跨零点必须写成文字，不能只靠颜色或角标（NFR-06）。</summary>
    public string ArrivalDayNote => Model.ArrivesNextDay ? "次日" : string.Empty;

    /// <summary>车型标记带首字母，不只靠颜色区分（NFR-06）。</summary>
    public string KindLabel => Core.Model.QueryCriteria.KindLabel(Core.Model.QueryCriteria.KindOf(TrainCode));

    /// <summary>区间是否只是全程车的一段。这会影响"多买几站"的可行方向，所以让人看得见。</summary>
    public bool IsPartialRoute =>
        !string.Equals(Model.From.Name, Model.StartStation.Name, StringComparison.Ordinal) ||
        !string.Equals(Model.To.Name, Model.EndStation.Name, StringComparison.Ordinal);

    /// <summary>
    /// 席别摘要：最多三个有代表性的席别，形如"二等 有 · 一等 候补 · 商务 3"。
    /// 有票的排前面，其次候补，最后无票——用户扫一眼就该看到能买的那个。
    /// </summary>
    public string SeatSummary
    {
        get
        {
            var ordered = Model.Seats
                .OrderBy(s => s.State switch
                {
                    SeatState.Available => 0,
                    SeatState.Waitlist => 1,
                    SeatState.NotYetOnSale => 2,
                    _ => 3,
                })
                .ThenBy(s => s.Class)
                .Take(3)
                .Select(s => $"{SeatLabel(s.Class)} {StateLabel(s.State)}");

            var joined = string.Join(" · ", ordered);
            return string.IsNullOrWhiteSpace(joined) ? text.Get("提示_官方未给出") : joined;
        }
    }

    /// <summary>最低价文本。缺价必须写明"官方未给出"，不能留一个破折号让人猜是没价还是没票（NFR-18）。</summary>
    public string LowestPriceText => Model.LowestPrice is { } p ? $"¥{p:0.##}" : text.Get("提示_无官方数据");

    /// <summary>有票时用于高亮的标记。无票/候补/未开售一律为 false。</summary>
    public bool HasTicket => Model.HasAnyTicket;

    /// <summary>不可网上购票才提供"多买几站"入口（FR-31）。</summary>
    public bool CanBuyOnline => Model.CanBuyOnline;

    /// <summary>放票时间（FR-33）。官方未给时整行不显示，不显示猜测值。</summary>
    public string OnSaleText => Model.OnSaleAt is { } t ? t.ToString("M月d日 HH:mm 起售") : string.Empty;

    /// <summary>尚未开售的车次单独标注，避免被误读成"卖光了"。</summary>
    public bool IsNotYetOnSale => Model.PurchaseState == PurchaseState.NotYetOnSale;

    private string SeatLabel(SeatClass seat) => seat switch
    {
        SeatClass.BusinessClass => text.Get("席别_商务座"),
        SeatClass.FirstClass => text.Get("席别_一等座"),
        SeatClass.SecondClass => text.Get("席别_二等座"),
        SeatClass.SpecialClass => text.Get("席别_特等座"),
        SeatClass.PremiumSoftSleeper => text.Get("席别_高级软卧"),
        SeatClass.SoftSleeper => text.Get("席别_软卧"),
        SeatClass.HardSleeper => text.Get("席别_硬卧"),
        SeatClass.SoftSeat => text.Get("席别_软座"),
        SeatClass.HardSeat => text.Get("席别_硬座"),
        SeatClass.Standing => text.Get("席别_无座"),
        SeatClass.Other => text.Get("席别_其他"),
        // 列 20 / 27 / 33 的中文名官方没给过，宁可写"未确认席别"也不猜一个，更不把枚举名 Unknown20 丢到屏幕上。
        _ => text.Get("席别_未确认"),
    };

    private string StateLabel(SeatState state) => state switch
    {
        SeatState.Available => text.Get("余票_有"),
        SeatState.SoldOut => text.Get("余票_无"),
        SeatState.Waitlist => text.Get("余票_候补"),
        SeatState.NotYetOnSale => text.Get("余票_未开售"),
        _ => text.Get("余票_无数据"),
    };
}
