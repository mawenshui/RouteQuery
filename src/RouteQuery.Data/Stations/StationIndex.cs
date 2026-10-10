using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.Data.Stations;

/// <summary>
/// 站点模糊匹配索引。对应 FR-01。
/// <para>匹配维度：站名精确/前缀/包含、全拼前缀、简拼前缀。打分排序后，
/// 同城站<b>逐条列出</b>而不是合并——"北京"和"北京南"是两个不同的乘车地点（FR-01 边界）。</para>
/// <para>之所以建索引而不是每次线性扫 3404 条：线性扫描本身只要零点几毫秒，
/// 但它给不出"前缀优于包含、站名短者通常为主站"这种排序质量。</para>
/// </summary>
public sealed class StationIndex : IStationRepository
{
    private readonly StationTable _table;
    private readonly List<Station> _all;
    private readonly Dictionary<string, Station> _byTelecode;

    public StationIndex(StationTable table)
    {
        _table = table;
        _all = [.. table.Items];
        _byTelecode = new Dictionary<string, Station>(StringComparer.Ordinal);
        foreach (var s in _all)
        {
            // 三字码重复时保留第一条而不是覆盖：官方码表理论唯一，出现重复说明数据源有问题，
            // 覆盖会让后续排查指向错误的站。
            _byTelecode.TryAdd(s.Telecode, s);
        }
    }

    public DateOnly SourceDate => _table.SourceDate;
    public bool IsLoaded => _all.Count > 0;
    public int Count => _all.Count;

    /// <summary>候选返回上限。</summary>
    public const int DefaultTake = 12;

    public IReadOnlyList<Station> Search(string keyword, int take = DefaultTake)
    {
        if (string.IsNullOrWhiteSpace(keyword) || _all.Count == 0) return [];

        var k = keyword.Trim();
        var kLower = k.ToLowerInvariant();
        var isAscii = true;
        foreach (var ch in k)
        {
            if (ch > 127) { isAscii = false; break; }
        }

        var scored = new List<(Station Station, int Score)>();
        foreach (var s in _all)
        {
            var score = Score(s, k, kLower, isAscii);
            if (score > 0) scored.Add((s, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Station.Name.Length)          // 同城内短名通常是主站，如"北京"优先于"北京南"
            .ThenBy(x => x.Station.Name, StringComparer.Ordinal)
            .Take(take)
            .Select(x => x.Station)
            .ToList();
    }

    public Station? FindByTelecode(string telecode) =>
        _byTelecode.TryGetValue(telecode, out var s) ? s : null;

    private int Score(Station s, string raw, string lowered, bool isAscii)
    {
        if (isAscii)
        {
            if (s.ShortPinyin.Equals(lowered, StringComparison.OrdinalIgnoreCase)) return 100;
            if (s.Pinyin.StartsWith(lowered, StringComparison.OrdinalIgnoreCase)) return 60;
            if (s.ShortPinyin.StartsWith(lowered, StringComparison.OrdinalIgnoreCase)) return 50;
            if (s.CityName.Equals(lowered, StringComparison.OrdinalIgnoreCase)) return 40;
            return 0;
        }

        if (s.Name == raw) return 120;
        if (s.Name.StartsWith(raw, StringComparison.Ordinal)) return 90;
        if (s.CityName.StartsWith(raw, StringComparison.Ordinal)) return 70;
        if (s.Name.Contains(raw, StringComparison.Ordinal)) return 30;
        return 0;
    }

    /// <summary>
    /// 取某个城市下的全部车站，用于"北京"这类城市级输入的一键展开。
    /// <para>依赖码表的城市代码字段——该字段是实测发现的，它让同城聚合不需要维护第二张映射表。</para>
    /// </summary>
    public IReadOnlyList<Station> SameCityAs(Station station) =>
        string.IsNullOrEmpty(station.CityCode)
            ? [station]
            : _all.Where(x => x.CityCode == station.CityCode).OrderBy(x => x.Name.Length).ToList();
}
