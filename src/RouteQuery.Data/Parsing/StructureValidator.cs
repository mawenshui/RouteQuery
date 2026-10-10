using RouteQuery.Core.Errors;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 响应结构自检。目的不是"解析得更宽容"，而是<b>让官方的字段变动第一时间以显式错误暴露</b>，
/// 而不是产出一堆字段错位但看起来很正常的数据（NFR-16、RISK-01）。
/// </summary>
public static class StructureValidator
{
    /// <summary>
    /// 校验一批记录是否符合 <see cref="LeftTicketColumnLayout"/> 声明的结构。
    /// </summary>
    /// <param name="records">已经按竖线切分好的列数组。</param>
    /// <exception cref="QueryException">任一项不合格即抛 <see cref="QueryErrorKind.ParseFailure"/>。
    /// 注意是<b>整批</b>失败而不是跳过该条：部分解析成功的产物比空列表危险得多。</exception>
    public static void AssertValid(IReadOnlyList<string[]> records)
    {
        if (records.Count == 0)
            return; // 空数组是合法的 NoResult，交由上层渲染空状态（FR-10），不是结构错误。

        foreach (var cols in records)
        {
            if (cols.Length != LeftTicketColumnLayout.ExpectedColumnCount)
                throw QueryException.ParseFailure(
                    $"列数不符：期望 {LeftTicketColumnLayout.ExpectedColumnCount}，实际 {cols.Length}");
        }

        foreach (var spec in LeftTicketColumnLayout.SelfCheckColumns)
        foreach (var cols in records)
        {
            var value = cols[spec.Index];
            if (!spec.Matches(value))
                throw QueryException.ParseFailure(
                    $"列 {spec.Index} 格式不符（值=\"{Trim(value)}\"，期望 {spec.Pattern}）");
        }
    }

    /// <summary>
    /// 判断一条响应文本是否可能承载车次数据。官方在两种截然不同的情形下都会返回同一段 HTML：
    /// 日期越界、以及被拒绝。因此调用方<b>必须</b>在发请求前完成日期校验（FR-04），
    /// 不能靠这里的返回值区分二者。
    /// </summary>
    public static bool LooksLikeHtml(string body)
    {
        var head = body.AsSpan(0, Math.Min(body.Length, 512));
        return head.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string s) => s.Length <= 24 ? s : s[..24] + "…";
}
