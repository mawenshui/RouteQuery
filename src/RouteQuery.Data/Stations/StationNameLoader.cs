using RouteQuery.Core.Model;

namespace RouteQuery.Data.Stations;

/// <summary>一条码表记录连同它的来源日期（用于状态栏显示"站点数据更新日期"，FR-18）。</summary>
/// <param name="SourceDate">该份码表对应的官方数据日期。</param>
/// <param name="Items">全部车站。</param>
public sealed record StationTable(DateOnly SourceDate, IReadOnlyList<Station> Items);

/// <summary>
/// 官方站点码表的解析。对应 FR-18。
/// <para>官方形态是 <c>var station_names = '@bjb|北京北|VAP|beijingbei|bjb|0|0357|北京|||…'</c>，
/// 以 <c>@</c> 分隔站点、以 <c>|</c> 分隔字段。实测 3404 条、UTF-8 编码。</para>
/// <para>字段序号（0 起，实测样本 <c>@bjb|北京北|VAP|beijingbei|bjb|0|0357|北京|||</c>）：
/// 0 检索键 / 1 站名 / 2 三字码 / 3 全拼 / 4 简拼 / 5 序号 / 6 城市代码 / 7 城市名。
/// <b>注意 0 位是检索键而不是站名</b>——把 0 当成站名会让整张码表错位一位。</para>
/// </summary>
public static class StationNameLoader
{
    private const int MinExpectedCount = 3000; // 明显少于这个数量即视为下载不完整，拒绝替换（FR-18 更新安全）

    /// <summary>从官方 JS 文本解析出码表。校验不过返回 null，由调用方决定报错还是用内置副本。</summary>
    public static StationTable? ParseFromOfficial(string jsText, DateOnly sourceDate)
    {
        var marker = jsText.IndexOf("station_names", StringComparison.Ordinal);
        if (marker < 0) return null;

        var start = jsText.IndexOf('\'', marker);
        var end = jsText.LastIndexOf('\'');
        if (start < 0 || end <= start) return null;

        var payload = jsText[(start + 1)..end];
        var stations = ParseEntries(payload);
        return stations.Count < MinExpectedCount ? null : new StationTable(sourceDate, stations);
    }

    /// <summary>解析已转换的内部 JSON 文本（应用内置副本走这条路，避免每次启动都解析 JS）。</summary>
    public static IReadOnlyList<Station> ParseEntries(string atSeparatedPayload)
    {
        var result = new List<Station>();
        foreach (var chunk in atSeparatedPayload.Split('@'))
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            var f = chunk.Split('|');
            if (f.Length < StationCodeTableLayout.MinFields) continue; // 字段不全的记录直接丢弃，不做补位猜测

            result.Add(new Station(
                Name: f[StationCodeTableLayout.Name],
                Telecode: f[StationCodeTableLayout.Telecode],
                Pinyin: f[StationCodeTableLayout.Pinyin],
                ShortPinyin: f[StationCodeTableLayout.ShortPinyin],
                CityCode: f[StationCodeTableLayout.CityCode],
                CityName: f[StationCodeTableLayout.CityName]));
        }
        return result;
    }

    /// <summary>码表是否达到可信规模。更新与启动加载都要过这一关。</summary>
    public static bool IsPlausible(IReadOnlyList<Station> items) => items.Count >= MinExpectedCount;
}
