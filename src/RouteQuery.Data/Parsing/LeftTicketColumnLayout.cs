using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// <c>API-02</c>（直达余票）结果的列布局。<b>全库唯一允许出现列下标的地方</b>（SPEC-004 一、硬约束 1）。
/// <para>列名与语义取自官方渲染脚本 <c>queryLeftTicket_end_js.js</c> 中把竖线串逐位赋值给具名字段的代码，
/// 并与官方页面 DOM 在同一瞬间逐格比对验证过；核实日期 2026-10-09。详见 SPEC-007 第二节。</para>
/// </summary>
public static class LeftTicketColumnLayout
{
    /// <summary>布局版本。官方字段变动时递增，并同步外置配置与样本。</summary>
    public const int LayoutVersion = 3;

    /// <summary>期望列数。跨 C/D/G/K/T/Z 六种车型、246 条记录实测恒为 58。</summary>
    public const int ExpectedColumnCount = 58;

    // ── 标识与时刻 ───────────────────────────────────────────
    public static readonly ColumnSpec SecretStr = new(0);
    public static readonly ColumnSpec TrainNo = new(2);
    public static readonly ColumnSpec TrainCode = new(3, Patterns.TrainCode);
    public static readonly ColumnSpec StartStationTelecode = new(4);
    public static readonly ColumnSpec EndStationTelecode = new(5);
    public static readonly ColumnSpec FromStationTelecode = new(6);
    public static readonly ColumnSpec ToStationTelecode = new(7);
    public static readonly ColumnSpec Departure = new(8, Patterns.Clock);
    public static readonly ColumnSpec Arrival = new(9, Patterns.Clock);
    public static readonly ColumnSpec Duration = new(10, Patterns.Clock);

    /// <summary>
    /// 可购状态列。实测取值域为 <c>Y</c> / <c>N</c> / <c>IS_TIME_NOT_BUY</c>，
    /// 因此<b>刻意不设格式校验</b>：给它加 <c>^[YN]$</c> 会让所有"尚未开售"的车次
    /// 触发整批 ParseFailure，那是比显示不精确更严重的后果（SPEC-007 v1.7 规则 6）。
    /// </summary>
    public static readonly ColumnSpec CanWebBuy = new(11);

    public static readonly ColumnSpec YpInfo = new(12);
    public static readonly ColumnSpec TrainDate = new(13, Patterns.CompactDate);

    /// <summary>区间在"始发—终到"站序中的序号，经停站接口要用（格式 yyyy 无关，两位序号）。</summary>
    public static readonly ColumnSpec FromStationNo = new(16);
    public static readonly ColumnSpec ToStationNo = new(17);

    // ── 余票：列 20 至 33，官方字段名为键 ──────────────────────
    /// <summary>
    /// 余票权威列。值为 <c>有</c> / <c>无</c> / 数字 / <c>*</c> / 空。
    /// <para><see cref="SeatClass.Unknown20"/>、<see cref="SeatClass.Unknown27"/>、
    /// <see cref="SeatClass.Unknown33"/> 三列官方页面没有独立展示列，中文标签未确认，
    /// 因此标记为 <see cref="ColumnProvenance.Unverified"/>：只透传，不参与筛选与排序。</para>
    /// </summary>
    public static readonly (SeatClass Class, ColumnSpec Spec)[] AvailabilityColumns =
    [
        (SeatClass.BusinessClass,        new(32)),   // swz_num
        (SeatClass.SpecialClass,         new(25)),   // tz_num
        (SeatClass.FirstClass,           new(31)),   // zy_num
        (SeatClass.SecondClass,          new(30)),   // ze_num
        (SeatClass.PremiumSoftSleeper,   new(21)),   // gr_num
        (SeatClass.SoftSleeper,          new(23)),   // rw_num
        (SeatClass.HardSleeper,          new(28)),   // yw_num
        (SeatClass.SoftSeat,             new(24)),   // rz_num
        (SeatClass.HardSeat,             new(29)),   // yz_num
        (SeatClass.Standing,             new(26)),   // wz_num
        (SeatClass.Other,                new(22)),   // qt_num
        (SeatClass.Unknown20,            new(20, Provenance: ColumnProvenance.Unverified)),   // gg_num
        (SeatClass.Unknown27,            new(27, Provenance: ColumnProvenance.Unverified)),   // yb_num
        (SeatClass.Unknown33,            new(33, Provenance: ColumnProvenance.Unverified)),   // srrb_num
    ];

    /// <summary>
    /// 无座与其他两个席别<b>不参与候补判定</b>——这是官方代码原文的条件
    /// （<c>"WZ_"!=dw &amp;&amp; "QT_"!=dw</c>），把它们算成候补会凭空造出不可购的"可候补"车次。
    /// </summary>
    public static readonly IReadOnlySet<SeatClass> NeverWaitlistable =
        new HashSet<SeatClass> { SeatClass.Standing, SeatClass.Other };

    // ── 候补与票价 ───────────────────────────────────────────
    /// <summary>车次级候补标记（"1" 表示该车次支持候补）。<b>不能单独</b>判定某席别可候补。</summary>
    public static readonly ColumnSpec HoubuTrainFlag = new(37);

    /// <summary>候补席别限制。官方只用它决定样式颜色，不参与"候补"标签判定。</summary>
    public static readonly ColumnSpec HoubuSeatLimit = new(38, Provenance: ColumnProvenance.ConfirmedByObservation);

    /// <summary>票价串（<c>yp_info_new</c>）。解码规则见 <see cref="PriceDecoder"/>。</summary>
    public static readonly ColumnSpec FareString = new(39);

    /// <summary>本车次席别短码序列，如 9MOO / 1431。与余票列名是两套编码（FR-13）。</summary>
    public static readonly ColumnSpec SeatTypes = new(35);

    /// <summary>放票时间（<c>sale_time</c>），形如 202610101245。部分车次为空。</summary>
    public static readonly ColumnSpec SaleTime = new(55);

    /// <summary><c>yp_ex</c>：曾经被误判为余票列。语义仍未确认，只透传，禁止接入任何判断。</summary>
    public static readonly ColumnSpec YpEx = new(34, Provenance: ColumnProvenance.Unverified);

    /// <summary>参与结构自检的三列：车次号、发车时刻、到达时刻。任一不合格式即判解析失败。</summary>
    public static ColumnSpec[] SelfCheckColumns => [TrainCode, Departure, Arrival];
}
