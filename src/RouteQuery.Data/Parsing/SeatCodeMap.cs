namespace RouteQuery.Data.Parsing;

/// <summary>
/// 票价侧席别短码（<c>seat_types</c> / 票价串里的 <c>9 M O 1 4 3 …</c>）到中文名，
/// 以及到余票列 <see cref="Core.Model.SeatClass"/> 的对应关系。
/// <para><b>这是与余票列名彼此独立的一套编码</b>（FR-13）：余票侧用 <c>ze_num</c> 这类列名，
/// 票价侧用 <c>O</c> 这类短码。把两套混为一谈会让某席别的余票配上另一席别的价格，
/// 于是界面出现"一等座 二等座价"这种看起来完全正常的错误。</para>
/// <para>未登记短码<b>不抛异常</b>，原样透传并记入日志（SPEC-005 三.⑨）。</para>
/// </summary>
public static class SeatCodeMap
{
    /// <summary>短码 → 中文名。键为官方短码，值为界面显示用中文。</summary>
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["9"] = "商务座",
        ["M"] = "一等座",
        ["O"] = "二等座",
        ["T"] = "特等座",
        ["P"] = "动卧",
        ["W"] = "无座",
        ["4"] = "硬卧",
        ["3"] = "软卧",
        ["6"] = "高级软卧",
        ["1"] = "硬座",
        ["2"] = "软座",
        ["D"] = "二等卧",
        ["I"] = "一等卧",
        ["J"] = "二等座",
        ["A"] = "优选一等座",
        ["F"] = "观光座",
    };

    /// <summary>短码 → 余票列席别。用于把票价对齐到余票行。</summary>
    private static readonly Dictionary<string, Core.Model.SeatClass> ToSeatClass = new()
    {
        ["9"] = Core.Model.SeatClass.BusinessClass,
        ["M"] = Core.Model.SeatClass.FirstClass,
        ["O"] = Core.Model.SeatClass.SecondClass,
        ["J"] = Core.Model.SeatClass.SecondClass,
        ["T"] = Core.Model.SeatClass.SpecialClass,
        ["W"] = Core.Model.SeatClass.Standing,
        ["4"] = Core.Model.SeatClass.HardSleeper,
        ["D"] = Core.Model.SeatClass.HardSleeper,
        ["3"] = Core.Model.SeatClass.SoftSleeper,
        ["I"] = Core.Model.SeatClass.SoftSleeper,
        ["6"] = Core.Model.SeatClass.PremiumSoftSleeper,
        ["1"] = Core.Model.SeatClass.HardSeat,
        ["2"] = Core.Model.SeatClass.SoftSeat,
        ["P"] = Core.Model.SeatClass.SoftSleeper,
        ["A"] = Core.Model.SeatClass.FirstClass,
        ["F"] = Core.Model.SeatClass.Other,
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
