namespace RouteQuery.Data.Parsing;

/// <summary>
/// 票价侧席别短码（票价串里的 <c>9 M O 1 4 3 …</c>）到中文名，以及到余票列
/// <see cref="Core.Model.SeatClass"/> 的对应关系。
/// <para><b>这是与余票列名彼此独立的一套编码</b>（FR-13）：余票侧用 <c>ze_num</c> 这类列名，
/// 票价侧用 <c>O</c> 这类短码。把两套混为一谈会让某席别的余票配上另一席别的价格，
/// 于是界面出现"一等座 二等座价"这种看起来完全正常的错误——而这张表在 2026-10-14 之前
/// 正是这么错的（见下方"这张表怎么被推翻的"）。</para>
/// <para><b>本表的出处是官方页面自己的两张表</b>，不是我们从样本反推的：</para>
/// <list type="bullet">
/// <item><c>window.seatTypeCodeForName</c>：短码 → 中文名，原文为
/// <c>{1:硬座, 2:软座, 3:硬卧, 4:软卧, 6:高级软卧, 9:商务座, P:特等座, A:高级动卧,
/// D:优选一等座, M:一等座, O:二等座, I:一等卧, J:二等卧, F:动卧, H:其他, WZ:无座, W:无座}</c>；</item>
/// <item><c>queryLeftTicket_end_js.js</c> 里生成单元格 <c>aria-label</c> 的函数（压缩后名为 <c>e</c>）：
/// 它逐个短码判断"哪个余票列该念出这条价格"，原文条件为
/// <c>SWZ_↔9、TZ_↔P、ZY_↔M、ZE_↔O|S、GR_↔6|A、RW_↔4|I、SRRB_↔F、YW_↔3|J、RZ_↔2、YZ_↔1</c>，
/// 而 <c>WZ_</c> 那一支<b>不看短码，只看记录尾部 ≥ 3000</b>（见 <see cref="PriceDecoder"/>）。</item>
/// </list>
/// <para>短码到余票列的对齐一律取官方 <c>e()</c> 的那个列，因为余票列才是界面上一行的身份；
/// 同一个列里的两种席别（如 <c>ZE_</c> 的二等座与二等包座）共用一个 <see cref="Core.Model.SeatClass"/>，
/// 与官方"取先命中的那条"的渲染顺序一致。</para>
/// <para><b>这张表怎么被推翻的</b>：旧表把 <c>3</c>/<c>4</c> 记成"软卧/硬卧"（正好反了），
/// <c>D</c> 记成二等卧（官方是优选一等座）、<c>J</c> 记成二等座（官方是二等卧）、
/// <c>A</c> 记成高级动卧却挂在优选一等座上、<c>P</c> 记成动卧（官方是特等座）、
/// <c>F</c> 记成观光座（官方是动卧），另有 <c>T</c> 这个官方根本不存在的短码。
/// 用仓库里 2026-10-09 采集的跨车型样本回测：旧表 22 条可比对记录里 11 条与余票列对不上，
/// 本表 29 条全对（判据是记录尾部那四位余票数必须等于该席别余票列的取值）。
/// 详见 SPEC-007 第二节"票价侧短码"与 2026-10-14 开发日志。</para>
/// <para>未登记短码<b>不抛异常</b>，原样透传并记入日志（SPEC-005 三.⑨）。
/// <c>F</c>（动卧）刻意留在表外：官方把它对齐到 <c>SRRB_</c> 列，而本仓库那一列（列 33）的
/// 中文标签仍未确认，把价格挂到一个连名字都没定的列上就是猜（NFR-18）。</para>
/// </summary>
public static class SeatCodeMap
{
    /// <summary>短码 → 中文名。键为官方短码，值为界面显示用中文。逐条抄自 <c>seatTypeCodeForName</c>。</summary>
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["1"] = "硬座",
        ["2"] = "软座",
        ["3"] = "硬卧",
        ["4"] = "软卧",
        ["6"] = "高级软卧",
        ["9"] = "商务座",
        ["A"] = "高级动卧",
        ["D"] = "优选一等座",
        ["F"] = "动卧",
        ["H"] = "其他",
        ["I"] = "一等卧",
        ["J"] = "二等卧",
        ["M"] = "一等座",
        ["O"] = "二等座",
        ["P"] = "特等座",
        ["S"] = "二等包座",
        ["W"] = "无座",
    };

    /// <summary>短码 → 余票列席别。用于把票价对齐到余票行。逐条抄自官方 <c>e()</c> 的列↔短码条件。</summary>
    private static readonly Dictionary<string, Core.Model.SeatClass> ToSeatClass = new()
    {
        ["9"] = Core.Model.SeatClass.BusinessClass,          // SWZ_ / swz_num
        ["P"] = Core.Model.SeatClass.SpecialClass,           // TZ_   / tz_num
        ["M"] = Core.Model.SeatClass.FirstClass,             // ZY_   / zy_num
        ["O"] = Core.Model.SeatClass.SecondClass,            // ZE_   / ze_num 二等座
        ["S"] = Core.Model.SeatClass.SecondClass,            // ZE_   / ze_num 二等包座（官方同列）
        ["6"] = Core.Model.SeatClass.PremiumSoftSleeper,     // GR_   / gr_num 高级软卧
        ["A"] = Core.Model.SeatClass.PremiumSoftSleeper,     // GR_   / gr_num 高级动卧（官方同列）
        ["4"] = Core.Model.SeatClass.SoftSleeper,            // RW_   / rw_num 软卧
        ["I"] = Core.Model.SeatClass.SoftSleeper,            // RW_   / rw_num 一等卧（官方同列）
        ["3"] = Core.Model.SeatClass.HardSleeper,            // YW_   / yw_num 硬卧
        ["J"] = Core.Model.SeatClass.HardSleeper,            // YW_   / yw_num 二等卧（官方同列）
        ["2"] = Core.Model.SeatClass.SoftSeat,               // RZ_   / rz_num
        ["1"] = Core.Model.SeatClass.HardSeat,               // YZ_   / yz_num
        ["D"] = Core.Model.SeatClass.PremiumFirstClass,      // GG_   / gg_num 优选一等座
        ["H"] = Core.Model.SeatClass.Other,                  // QT_   / qt_num
        ["W"] = Core.Model.SeatClass.Standing,               // WZ_   / wz_num
    };

    /// <summary>已登记的短码集合，供测试断言覆盖范围。</summary>
    public static IReadOnlyCollection<string> KnownCodes => Labels.Keys;

    /// <summary>取中文名；未登记时返回原短码本身，绝不返回 null 或空串。</summary>
    public static string LabelOf(string code) => Labels.TryGetValue(code, out var l) ? l : code;

    /// <summary>
    /// 取对应的余票列席别；未登记时返回 null，调用方应把该条价格当作"无法对齐"处理，
    /// 而不是猜一个最接近的席别（NFR-18）。
    /// </summary>
    public static Core.Model.SeatClass? SeatClassOf(string code) =>
        ToSeatClass.TryGetValue(code, out var c) ? c : null;

    /// <summary>该短码是否为未登记的新值（用于写诊断日志）。</summary>
    public static bool IsUnknown(string code) => !Labels.ContainsKey(code);
}
