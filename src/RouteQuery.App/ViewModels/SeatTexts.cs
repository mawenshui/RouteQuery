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
        SeatClass.PremiumFirstClass => text.Get("席别_优选一等座"),
        // 列 27 / 33 的中文名官方没给过，宁可写"未确认席别"也不猜，更不把枚举名丢给用户（DEC-16）。
        _ => text.Get("席别_未确认"),
    };

    /// <summary>
    /// 余票状态的中文说法。<b>官方给的是具体数字时，原样把数字摆出来</b>——需求 6.3 的展示规则写得很死：
    /// "数字（<c>3</c>）原样显示；<c>有</c>/<c>无</c> 原样显示"。官方把 20 张以上折成"有"、剩下的写成具体数，
    /// 这个区别本身就是信息：一律写成"有"，"只剩 5 张"和"多到不数"在界面上就长得一模一样，
    /// 而用户要按这个决定要不要现在买。
    /// <para>2026-10-20 与官方页面逐格比对时抓到的就是这一条：官方 G807 商务座那一格写 <c>7</c>，
    /// 界面写"有"——列 32 里明明是 <c>7</c>，是显示层把它抹平的。</para>
    /// </summary>
    public static string State(SeatState state, string raw, ITextProvider text) => state switch
    {
        SeatState.Available => int.TryParse(raw, out var count) && count > 0 ? raw : text.Get("余票_有"),
        SeatState.SoldOut => text.Get("余票_无"),
        SeatState.Waitlist => text.Get("余票_候补"),
        SeatState.NotYetOnSale => text.Get("余票_未开售"),
        _ => text.Get("提示_官方未给出"),
    };

    /// <summary>有票的排前面、其次候补，让用户扫一眼就看到能买的那个。</summary>
    public static string Summary(IEnumerable<SeatAvailability> seats, ITextProvider text) =>
        string.Join(" · ", Lines(seats, text));

    /// <summary>
    /// 官方在这一行给出的<b>全部</b>席别，每条形如"商务座 12 · ¥2337"（FR-49）。
    /// <para>之前这里是"取前 3 个有代表性的席别"，而丢掉的恰恰是用户要看的：
    /// 官方给了 5 个席别与 5 个票价，界面只摆 3 个还不带价，读起来就像"这个工具的数据不如官网全"。
    /// 这些数字全部来自同一次响应，<b>多显示不需要多请求</b>。</para>
    /// <para>票价缺失时写"官方未给出"而不是留空——空一格会被读成"免费"或"没有这个席别"。</para>
    /// </summary>
    public static IReadOnlyList<string> Lines(IEnumerable<SeatAvailability> seats, ITextProvider text) =>
        seats
            .OrderBy(s => s.State switch
            {
                SeatState.Available => 0,
                SeatState.Waitlist => 1,
                SeatState.NotYetOnSale => 2,
                _ => 3,
            })
            .ThenBy(s => s.Class)
            .Select(s => $"{Seat(s.Class, text)} {State(s.State, s.Raw, text)} · " +
                         (s.Price is { } p ? $"¥{p:0.##}" : text.Get("提示_无官方数据")))
            .ToList();
}
