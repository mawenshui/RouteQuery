using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 把某个席别列的官方原始值翻译成 <see cref="SeatState"/>。
/// <para>判定式取自官方渲染脚本原文，不是推测：</para>
/// <code>
/// if ("无" == 值 &amp;&amp; "WZ_" != 席别 &amp;&amp; "QT_" != 席别 &amp;&amp; 车次支持候补) → 显示"候补"
/// </code>
/// <para>三条要点：① <b>列 37 是车次级标记</b>，实测 122 条中 97 条为 1、其中 48 条仍有票，
/// 所以它不能单独决定候补；② 列 38 只影响样式，不参与标签；③ <b>星号 <c>*</c> 不是余票值</b>，
/// 它只出现在"尚未开售"的行里，而未开售的判据是列 11 而不是席别列（SPEC-007 v1.7 规则 5、7）。</para>
/// </summary>
public static class AvailabilityDecoder
{
    /// <summary>
    /// <param name="raw">席别列原始值（<c>有</c> / <c>无</c> / 数字 / <c>*</c> / 空）。</param>
    /// <param name="purchase">该车次的可购状态（列 11）。未开售时席别列没有可信数据。</param>
    /// <param name="seatClass">席别类别，用于排除无座与其他（官方不为其标候补）。</param>
    /// <param name="trainSupportsWaitlist">列 37 是否为 <c>1</c>。</param>
    public static SeatState Decode(
        string raw,
        PurchaseState purchase,
        SeatClass seatClass,
        bool trainSupportsWaitlist)
    {
        if (purchase == PurchaseState.NotYetOnSale)
            return SeatState.NotYetOnSale;

        if (string.IsNullOrEmpty(raw))
            return SeatState.SoldOut;

        switch (raw)
        {
            case "有":
                return SeatState.Available;

            case "无":
                // 官方刻意不给无座和其他席别标候补，照抄这个条件，不做"顺手放宽"。
                var canWaitlist = trainSupportsWaitlist
                    && !LeftTicketColumnLayout.NeverWaitlistable.Contains(seatClass);
                return canWaitlist ? SeatState.Waitlist : SeatState.SoldOut;

            case "*":
                // 未开售行里才会出现，但列 11 已经判过；走到这里说明两列不一致，
                // 按"不知道"处理而不是当成无票（NFR-18）。
                return SeatState.Unknown;

            default:
                return int.TryParse(raw, out var count)
                    ? count > 0 ? SeatState.Available : SeatState.SoldOut
                    : SeatState.Unknown;
        }
    }
}
