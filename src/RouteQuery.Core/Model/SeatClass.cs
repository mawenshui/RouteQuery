namespace RouteQuery.Core.Model;

/// <summary>
/// 席别（座位/铺位类别）。成员名对应官方 <c>API-02</c> 的**余票列字段名**
/// （见 SPEC-007 第二节"列 20–33"表），不是票价侧的短码。
/// <para>余票列名与票价席别码是两套独立编码，必须各自建表，不得混用（FR-13）。</para>
/// </summary>
public enum SeatClass
{
    /// <summary>gg_num（列 20）。官方页面无独立展示列，中文标签未确认。</summary>
    Unknown20,

    /// <summary>gr_num（列 21）高级软卧。</summary>
    PremiumSoftSleeper,

    /// <summary>qt_num（列 22）其他。</summary>
    Other,

    /// <summary>rw_num（列 23）软卧 / 动卧 / 一等卧。</summary>
    SoftSleeper,

    /// <summary>rz_num（列 24）软座。</summary>
    SoftSeat,

    /// <summary>tz_num（列 25）特等座。</summary>
    SpecialClass,

    /// <summary>wz_num（列 26）无座。</summary>
    Standing,

    /// <summary>yb_num（列 27）。中文标签未确认。</summary>
    Unknown27,

    /// <summary>yw_num（列 28）硬卧 / 二等卧。</summary>
    HardSleeper,

    /// <summary>yz_num（列 29）硬座。</summary>
    HardSeat,

    /// <summary>ze_num（列 30）二等座 / 二等包座。</summary>
    SecondClass,

    /// <summary>zy_num（列 31）一等座。</summary>
    FirstClass,

    /// <summary>swz_num（列 32）商务座。</summary>
    BusinessClass,

    /// <summary>srrb_num（列 33）。中文标签未确认。</summary>
    Unknown33,
}
