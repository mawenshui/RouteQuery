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

    /// <summary>结构自检：单段必须齐这些字段。</summary>
    public static readonly string[] RequiredLegFields =
    [
        "station_train_code", "train_no", "from_station_telecode", "from_station_name",
        "to_station_telecode", "to_station_name", "start_time", "arrive_time", "lishi", "day_difference",
    ];
}
