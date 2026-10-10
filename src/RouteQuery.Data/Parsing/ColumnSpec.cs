using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 一列的定义：位置 + 可选的格式校验 + 来源标记。
/// <para>来源标记不是注释装饰：<see cref="ColumnProvenance.ConfirmedByOfficialScript"/> 的列才允许驱动
/// 筛选、排序和金额，<see cref="ColumnProvenance.ConfirmedByObservation"/> 或
/// <see cref="ColumnProvenance.Unverified"/> 的列只能透传（SPEC-007 v1.7 规则 1、5）。</para>
/// </summary>
/// <param name="Index">竖线切分后的列下标。<b>全库唯一允许出现裸下标的地方就是这个表的构造参数。</b></param>
/// <param name="Pattern">可选的格式校验正则；为空表示不校验格式。</param>
/// <param name="Provenance">结论来源。</param>
public sealed record ColumnSpec(int Index, string? Pattern = null, ColumnProvenance Provenance = ColumnProvenance.ConfirmedByOfficialScript)
{
    /// <summary>
    /// 用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 而不是普通 Dictionary：
    /// 本类型是静态共享的，而应用会有并发解析（测试并行时就已经真的炸过一次）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    /// <summary>该列是否声明了格式要求。</summary>
    public bool HasPattern => Pattern is not null;

    /// <summary>值是否符合本列格式。未声明格式时恒为 true。</summary>
    public bool Matches(string value)
    {
        if (Pattern is null) return true;
        var regex = Cache.GetOrAdd(Pattern, p => new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant));
        return regex.IsMatch(value);
    }
}

/// <summary>列语义的结论来源，决定它能不能参与判断。</summary>
public enum ColumnProvenance
{
    /// <summary>官方渲染脚本里的字段赋值代码直接证实。可用于筛选、排序、金额。</summary>
    ConfirmedByOfficialScript,

    /// <summary>靠取值推测并与页面比对过。可用于展示，不可用于金额。</summary>
    ConfirmedByObservation,

    /// <summary>语义未确认。只能原样透传，禁止参与任何判断（由 ColumnProvenanceTests 强制）。</summary>
    Unverified,
}

/// <summary>正则常量集中处，避免同一模式在多处字面量漂移。</summary>
internal static class Patterns
{
    /// <summary>
    /// 车次号：<b>可选的单字母前缀 + 1 到 4 位数字</b>。
    /// <para>这里的可选前缀是实测纠正的结果——普客列车是纯数字车次（如 1461），
    /// 而最初设计的 <c>^[GDCZTKLY]\d{1,4}$</c> 会把它判成结构异常并导致整批不出数（SPEC-007 v1.7 规则 1）。</para>
    /// </summary>
    public const string TrainCode = "^[A-Z]?[0-9]{1,4}$";

    /// <summary>时刻：24 小时制 HH:mm。</summary>
    public const string Clock = "^[0-9]{2}:[0-9]{2}$";

    /// <summary>日期：yyyyMMdd。</summary>
    public const string CompactDate = "^[0-9]{8}$";
}
