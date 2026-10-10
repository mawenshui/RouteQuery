using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// 席别名与余票状态的中文说法。<b>直达行、扩展行、中转行共用这一份</b>。
/// <para>之前三处各写了一个 switch，而"未确认席别不得显示枚举名"这类口径一旦只改一处，
/// 另外两处就会悄悄把 <c>Unknown20</c> 打到屏幕上——同一件事有三个说法，正是这种地方长出来的。</para>
/// </summary>
public static class SeatTexts
{
    public static string Seat(SeatClass seat, ITextProvider text) => seat switch
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
        // 列 20 / 27 / 33 的中文名官方没给过，宁可写"未确认席别"也不猜，更不把枚举名丢给用户（DEC-16）。
        _ => text.Get("席别_未确认"),
    };

    public static string State(SeatState state, ITextProvider text) => state switch
    {
        SeatState.Available => text.Get("余票_有"),
        SeatState.SoldOut => text.Get("余票_无"),
        SeatState.Waitlist => text.Get("余票_候补"),
        SeatState.NotYetOnSale => text.Get("余票_未开售"),
        SeatState.Unknown => text.Get("提示_官方未给出"),
        _ => text.Get("提示_官方未给出"),
    };

    /// <summary>有票的排前面、其次候补，让用户扫一眼就看到能买的那个。</summary>
    public static string Summary(IEnumerable<SeatAvailability> seats, ITextProvider text, int take = 3)
    {
        var joined = string.Join(" · ", seats
            .OrderBy(s => s.State switch
            {
                SeatState.Available => 0,
                SeatState.Waitlist => 1,
                SeatState.NotYetOnSale => 2,
                _ => 3,
            })
            .ThenBy(s => s.Class)
            .Take(take)
            .Select(s => $"{Seat(s.Class, text)} {State(s.State, text)}"));

        return joined.Length == 0 ? text.Get("提示_官方未给出") : joined;
    }
}
