namespace RouteQuery.Core.Ports;

/// <summary>
/// 一条收藏的线路。对应 FR-16。
/// <para><b>刻意不存日期</b>：收藏的是"我常走这一程"，不是"我 10 月 10 日走过这一程"。
/// 存了日期就会有两种坏结果——要么回填一个早已过去的日期，要么每次回填都得先判断日期还有效不有效。
/// 日期每次由用户在界面上重新确认，这是需求里写死的口径。</para>
/// </summary>
/// <param name="Name">用户自己起的名字，如"回家·北京南→徐州东"。</param>
public sealed record SavedRoute(string Name, string FromTelecode, string ToTelecode);

/// <summary>一条查询历史。对应 FR-17——多存了日期与结果条数，因为它的用途是"刚才那次"。</summary>
/// <param name="ResultCount">官方返回的车票数。没查到就是 0，出错时<b>不记</b>（错误不配占一条历史）。</param>
public sealed record QueryHistoryEntry(
    string FromTelecode, string ToTelecode, DateOnly TravelDate, int ResultCount, DateTimeOffset At);

/// <summary>
/// 常用线路收藏。上限 20 条是产品规则而不是性能考虑：超过这个数的收藏基本等于没有收藏，
/// 而"达上限明确提示"比"静默丢弃"或"允许无限堆"都更诚实。
/// </summary>
public interface IRouteBook
{
    /// <summary>收藏上限（FR-16）。</summary>
    public const int MaxEntries = 20;

    IReadOnlyList<SavedRoute> All { get; }

    /// <summary>加入一条。返回 false 表示已达上限——调用方<b>必须</b>把这句话告诉用户。</summary>
    bool Add(SavedRoute route);

    /// <summary>按名字删除。删除不得影响其余条目的顺序（FR-16 验收②）。</summary>
    bool Remove(string name);
}

/// <summary>查询历史。只保留最近 30 条，超出淘汰最旧的（FR-17 验收②）。</summary>
public interface IQueryHistory
{
    public const int MaxEntries = 30;

    /// <summary>按时间倒序（最近的在前）。</summary>
    IReadOnlyList<QueryHistoryEntry> All { get; }

    void Record(QueryHistoryEntry entry);

    /// <summary>清空。必须落到文件层面，而不是只在内存里假装没有（FR-17 验收③）。</summary>
    void Clear();
}
