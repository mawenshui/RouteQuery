using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 中转接口（<c>API-03 = /lcquery/queryG</c>）的字段表。
/// <para>这个响应是<b>具名字段</b>的 JSON，不像余票接口那样依赖列位，所以这里没有下标、
/// 只有"官方字段名 → 本项目含义"的映射。它仍然必须集中成一张表：字段名散在解析代码里，
/// 官方改名时就只能靠全文搜索来救。</para>
/// <para>依据：2026-10-10 由用户浏览器 Network 面板确认路径与请求头，再用同一会话取到
/// 一份真实响应（脱敏样本见 <c>测试/接口样本/lcQuery-中转样本.json</c>）。</para>
/// </summary>
public static class TransferFieldMap
{
    /// <summary>
    /// 席别余票字段名。与余票接口列 20–33 是<b>同一套官方字段名</b>，因此席别语义可以直接复用
    /// <see cref="LeftTicketColumnLayout"/>；但本响应里没有 <c>yp_info_new</c>，所以没有票价。
    /// </summary>
    public static readonly (SeatClass Class, string Field)[] SeatFields =
    [
        (SeatClass.BusinessClass, "swz_num"),
        (SeatClass.SpecialClass, "tz_num"),
        (SeatClass.FirstClass, "zy_num"),
        (SeatClass.SecondClass, "ze_num"),
        (SeatClass.PremiumSoftSleeper, "gr_num"),
        (SeatClass.SoftSleeper, "rw_num"),
        (SeatClass.HardSleeper, "yw_num"),
        (SeatClass.SoftSeat, "rz_num"),
        (SeatClass.HardSeat, "yz_num"),
        (SeatClass.Standing, "wz_num"),
        (SeatClass.Other, "qt_num"),
    ];

    /// <summary>结构自检：方案层必须齐这些字段，缺任何一个都判 <c>ParseFailure</c> 而不是"少显示一列"。</summary>
    public static readonly string[] RequiredPlanFields =
    [
        "from_station_code", "from_station_name", "middle_station_code", "middle_station_name",
        "end_station_code", "end_station_name", "first_train_no", "second_train_no",
        "train_date", "arrive_date", "start_time", "arrive_time",
        "all_lishi", "all_lishi_minutes", "wait_time", "wait_time_minutes",
        "same_station", "isOutStation", "fullList",
    ];

    /// <summary>
    /// <c>same_station</c> / <c>isOutStation</c> / <c>lCWaitTime</c> 这三个 0/1 字段
    /// <b>语义未确认，不参与任何展示判断</b>：2026-10-10 的 10 份样本里它们恒为 "0"，
    /// 而同一批方案有 9 个 <c>same_train="Y"</c>（同一趟车继续坐，显然不需要换车站）——
    /// 说明 "0" 不等于"需要换站"。在拿到能区分取值的样本之前，界面只说候车时长。
    /// </summary>
    public static readonly string[] SemanticsUnconfirmed = ["same_station", "isOutStation", "lCWaitTime"];

    /// <summary>结构自检：单段必须齐这些字段。</summary>
    public static readonly string[] RequiredLegFields =
    [
        "station_train_code", "train_no", "from_station_telecode", "from_station_name",
        "to_station_telecode", "to_station_name", "start_time", "arrive_time", "lishi", "day_difference",
        // 每段自己的乘车日期。FR-15 展开该段经停时必须拿它当 depart_date；缺了它就没有"这一段的哪天"，
        // 只会有我们推算出来的哪天。
        "start_train_date",
    ];

    /// <summary>段内日期格式。<b>与方案层的 <c>train_date</c> 不是同一种写法</b>：
    /// 方案层是 <c>yyyy-MM-dd</c>，段里是 <c>yyyyMMdd</c>。混用是本项目踩过的坑（SPEC-007 记过一次）。</summary>
    public const string LegDateFormat = "yyyyMMdd";
}
