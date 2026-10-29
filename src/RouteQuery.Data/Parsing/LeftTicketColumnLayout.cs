using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// <c>API-02</c>（直达余票）结果的列布局。<b>全库唯一允许出现列下标的地方</b>（SPEC-004 一、硬约束 1）。
/// <para>列名与语义取自官方渲染脚本 <c>queryLeftTicket_end_js.js</c> 中把竖线串逐位赋值给具名字段的代码，
/// 并与官方页面 DOM 在同一瞬间逐格比对验证过；核实日期 2026-10-09，v4 变动核实日期 2026-10-28。
/// 详见 SPEC-007 第二节。</para>
/// </summary>
public static class LeftTicketColumnLayout
{
    /// <summary>布局版本。官方字段变动时递增，并同步外置配置与样本。</summary>
    public const int LayoutVersion = 4;

    /// <summary>
    /// 期望列数。v3 时代（≤2026-10-20 实测）跨 C/D/G/K/T/Z 六种车型、246 条记录恒为 58；
    /// <b>2026-10-28 实测变成 59</b>——新列插在下标 57，原第 57 列（恒空）被推到 58。
    /// <para>本应用读取的最深下标是 <see cref="SaleTime"/>（55），所以<b>没有任何已用字段发生位移</b>。
    /// 但列数仍然要改准：这条断言的意义就是"官方一改结构，我们立刻停止出数并说清楚"，
    /// 而不是"反正没影响就放宽"。放宽一次，下一次真位移时就没有刹车了（AGENTS 六.2、七.6）。</para>
    /// <para>列位对齐证据与两份样本的逐位对照见
    /// <c>测试/接口样本/leftTicket-跨车型样本-v4-59列-20261028.json</c> 的 <c>_元信息</c>。</para>
    /// </summary>
    public const int ExpectedColumnCount = 59;

    // ── 标识与时刻 ───────────────────────────────────────────
    public static readonly ColumnSpec SecretStr = new(0);

    /// <summary>官方给这条记录的一句状态话。正常车次这里是"预订"；
    /// 运行图调整时官方在这里直接写"列车运行图调整,暂停发售"，并把时刻填成哨兵值。</summary>
    public static readonly ColumnSpec StatusMessage = new(1);

    public static readonly ColumnSpec TrainNo = new(2);
    public static readonly ColumnSpec TrainCode = new(3, Patterns.TrainCode);
    public static readonly ColumnSpec StartStationTelecode = new(4);
    public static readonly ColumnSpec EndStationTelecode = new(5);
    public static readonly ColumnSpec FromStationTelecode = new(6);
    public static readonly ColumnSpec ToStationTelecode = new(7);
    public static readonly ColumnSpec Departure = new(8, Patterns.ClockTime);
    public static readonly ColumnSpec Arrival = new(9, Patterns.ClockTime);
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
    /// <para><see cref="SeatClass.PremiumFirstClass"/>（列 20）已按官方页面的列头与单元格 id 确认为"优选一等座"
    /// （SPEC-007 v1.8），来源类型是观察确认而非官脚本书证。<see cref="SeatClass.Unknown27"/> 与
    /// <see cref="SeatClass.Unknown33"/> 两列官方没有给出展示列，中文标签仍未确认，
    /// 因此标记为 <see cref="ColumnProvenance.Unverified"/>：只透传，界面写"未确认席别"。</para>
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
        (SeatClass.PremiumFirstClass,    new(20, Provenance: ColumnProvenance.ConfirmedByObservation)),   // gg_num 优选一等座
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

    /// <summary>
    /// v4 新增列（2026-10-28 实测）。取值形如 <c>0##0#2</c> / <c>Q0210##1#1</c>，
    /// 其中 <c>Q0210</c>、<c>Q0304</c> 这类片段也出现在下标 46 的复合串里，疑似与席位/静音车厢配置有关。
    /// <para><b>没有任何官方脚本或页面列头证据，所以本列不接入任何判断，也不显示给用户</b>——
    /// 登记它只为两件事：① 让"59 列里有两列我们不知道是什么"这件事在代码里是显式的，
    /// 而不是靠注释里一句"末尾多了一列"；② 将来官方给出证据时，下标已经占好位。</para>
    /// </summary>
    public static readonly ColumnSpec Unknown57 = new(57, Provenance: ColumnProvenance.Unverified);

    /// <summary>v3 时代的最后一列（恒空），v4 里被 <see cref="Unknown57"/> 挤到下标 58。同样不读取。</summary>
    public static readonly ColumnSpec Unknown58 = new(58, Provenance: ColumnProvenance.Unverified);

    /// <summary>参与结构自检的三列：车次号、发车时刻、到达时刻。任一不合格式即判解析失败。</summary>
    public static ColumnSpec[] SelfCheckColumns => [TrainCode, Departure, Arrival];

    /// <summary>
    /// 官方在"运行图调整、暂停发售"时会塞进一条占位记录：车次号是真的，但时刻是哨兵值
    /// <c>24:00 / 24:00 / 99:59</c>、可购状态是 <c>IS_TIME_NOT_BUY</c>、列 1 写着那句中文。
    /// <para>2026-10-12 实测（北京南→上海虹桥 2026-10-13，54 条记录里 1 条是它）。
    /// 它<b>不是</b>结构漂移：把它当漂移会让整趟查询不出数，用户看到的是"这个版本查不了"，
    /// 而官方其实给了 53 趟真数据。所以这里精确识别这一种形态，把它单独摘出去并计数说明；
    /// 任何其它不合格的时刻仍然判 <c>ParseFailure</c>，不许顺手放宽。</para>
    /// </summary>
    public static bool IsTimetableSuspended(string[] cols) =>
        cols.Length == ExpectedColumnCount
        && cols[Departure.Index] == "24:00"
        && cols[Arrival.Index] == "24:00"
        && cols[Duration.Index] == "99:59"
        && cols[CanWebBuy.Index] == "IS_TIME_NOT_BUY"
        && cols[StatusMessage.Index].Length > 0;
}
