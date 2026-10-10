using RouteQuery.Core.Logic;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// "多买几站"结果面板里的一行。对应 FR-28。
/// <para>三条显示纪律：① <b>多买几站与多花多少钱必须同一行可见</b>；
/// ② 算不出来写"无法计算"，<b>不允许显示 0 或留空</b>；
/// ③ 算出负值显示"价格数据异常"而不是那个数字——那说明我们解析错了，
/// 把一个错金额交给用户去决定多付钱，是这个应用最不该发生的事。</para>
/// </summary>
public sealed class ExtensionRowViewModel(ExtensionOutcome outcome, TrainJourney target, ITextProvider text)
    : Mvvm.ViewModelBase
{
    public ExtensionCandidate Candidate { get; } = outcome.Candidate;

    /// <summary>对齐席别：目标区间上第一个"有票且有价"的席别，退而求其次取有票的、再取有价的。</summary>
    private readonly SeatClass _seat = PickSeat(target);

    private readonly PriceDeltaResult _delta =
        PriceDeltaCalculator.Compute(outcome.Candidate, target, outcome.Journey, PickSeat(target));

    /// <summary>如"往前多坐 1 站，往后多坐 1 站"。</summary>
    public string ExtraText
    {
        get
        {
            var parts = new List<string>();
            if (Candidate.BoardAhead > 0) parts.Add($"往前多坐 {Candidate.BoardAhead} 站");
            if (Candidate.RideBeyond > 0) parts.Add($"往后多坐 {Candidate.RideBeyond} 站");
            return string.Join("，", parts);
        }
    }

    /// <summary>票面区间——真正能刷身份证进站的那一段。</summary>
    public string RouteText => $"{Candidate.BoardStation.StationName} → {Candidate.AlightStation.StationName}";

    public string SeatSummary
    {
        get
        {
            // "这趟车在该区间不售"和"官方没给余票"是两件事，不能都说成前者（AGENTS 七.6）。
            if (outcome.Journey is not { } j) return text.Get("扩展_不可售");
            if (j.Seats.Count == 0) return text.Get("提示_官方未给出");

            var picked = j.Seats
                .OrderBy(s => s.State switch { SeatState.Available => 0, SeatState.Waitlist => 1, _ => 2 })
                .ThenBy(s => s.Class)
                .Take(3)
                .Select(s => $"{Label(s.Class)} {StateLabel(s.State)}");
            var joined = string.Join(" · ", picked);
            return string.IsNullOrWhiteSpace(joined) ? text.Get("提示_官方未给出") : joined;
        }
    }

    public bool IsNotSold => outcome.NotSoldHere;

    /// <summary>可购且算得出差额才推荐，异常值不推荐。</summary>
    public bool IsBuyable => outcome.Journey is { CanBuyOnline: true } && _delta.Delta is >= 0 && !_delta.IsAnomalous;

    public string PriceText =>
        outcome.Journey?.Seats.FirstOrDefault(s => s.Class == _seat)?.Price is { } p ? $"¥{p:0.##}" : text.Get("扩展_无法计算");

    public string DeltaText => _delta.Delta switch
    {
        null => text.Get("扩展_无法计算"),
        _ when _delta.IsAnomalous => text.Get("扩展_异常"),
        var d => $"多花 ¥{d:0.##}",
    };

    public string SeatName => Label(_seat);

    public bool IsAnomalous => _delta.IsAnomalous;

    private static SeatClass PickSeat(TrainJourney target) =>
        target.Seats.FirstOrDefault(s => s.IsAvailable && s.Price is not null)?.Class
        ?? target.Seats.FirstOrDefault(s => s.IsAvailable)?.Class
        ?? target.Seats.FirstOrDefault(s => s.Price is not null)?.Class
        ?? SeatClass.SecondClass;

    private string Label(SeatClass seat) => seat switch
    {
        SeatClass.BusinessClass => text.Get("席别_商务座"),
        SeatClass.FirstClass => text.Get("席别_一等座"),
        SeatClass.SecondClass => text.Get("席别_二等座"),
        SeatClass.Standing => text.Get("席别_无座"),
        SeatClass.HardSeat => text.Get("席别_硬座"),
        SeatClass.HardSleeper => text.Get("席别_硬卧"),
        SeatClass.SoftSleeper => text.Get("席别_软卧"),
        SeatClass.SoftSeat => text.Get("席别_软座"),
        SeatClass.PremiumSoftSleeper => text.Get("席别_高级软卧"),
        SeatClass.SpecialClass => text.Get("席别_特等座"),
        SeatClass.Other => text.Get("席别_其他"),
        _ => text.Get("席别_未确认"),
    };

    private string StateLabel(SeatState state) => state switch
    {
        SeatState.Available => text.Get("余票_有"),
        SeatState.Waitlist => text.Get("余票_候补"),
        SeatState.NotYetOnSale => text.Get("余票_未开售"),
        SeatState.SoldOut => text.Get("余票_无"),
        _ => text.Get("余票_无数据"),
    };
}
