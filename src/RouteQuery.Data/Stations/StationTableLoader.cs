using System.Text.Json;
using System.Text.Json.Serialization;
using RouteQuery.Core.Model;

namespace RouteQuery.Data.Stations;

/// <summary>内置码表 JSON 的一条记录。</summary>
internal sealed record StationRow(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Telecode")] string Telecode,
    [property: JsonPropertyName("Pinyin")] string Pinyin,
    [property: JsonPropertyName("ShortPinyin")] string ShortPinyin,
    [property: JsonPropertyName("CityCode")] string CityCode,
    [property: JsonPropertyName("CityName")] string CityName);

/// <summary>内置码表文档。</summary>
internal sealed record StationDoc(
    [property: JsonPropertyName("SourceDate")] string SourceDate,
    [property: JsonPropertyName("Count")] int Count,
    [property: JsonPropertyName("Stations")] List<StationRow> Stations);

/// <summary>
/// 内置与本地码表的加载。对应 FR-18。
/// <para>两条路都要能失败得明确：内置副本读不到就是启动期致命错误；手动更新下载的新表校验不过
/// 则<b>保留旧表</b>并提示，绝不允许出现"更新失败 → 码表损坏 → 应用彻底不可用"（FR-18 验收②）。</para>
/// </summary>
public static class StationTableLoader
{
    /// <summary>内置码表相对程序目录的路径。</summary>
    public const string BundledRelativePath = "Resources/stations.json";

    /// <summary>加载内置码表。</summary>
    /// <exception cref="InvalidOperationException">内置副本缺失或规模异常。此时应用必须禁用查询并说明原因（BF-03）。</exception>
    public static StationTable LoadBundled(string? baseDirectory = null)
    {
        var path = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, BundledRelativePath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"内置站点数据缺失：{path}");

        var doc = Deserialize(ReadUtf8(path));
        if (doc is null || !StationNameLoader.IsPlausible(doc.Stations.Select(ToStation).ToList()))
            throw new InvalidOperationException("内置站点数据规模异常，可能被截断");

        var items = doc.Stations.Select(ToStation).ToList();
        return new StationTable(DateOnly.Parse(doc.SourceDate, System.Globalization.CultureInfo.InvariantCulture), items);
    }

    /// <summary>
    /// 校验一份下载到的新码表是否可安全替换现有表。
    /// 返回 null 表示校验不过——调用方必须保留旧表（FR-18 更新安全）。
    /// </summary>
    public static StationTable? ValidateDownloaded(string json, DateOnly sourceDate)
    {
        var doc = Deserialize(json);
        if (doc is null) return null;
        var items = doc.Stations.Select(ToStation).ToList();
        if (!StationNameLoader.IsPlausible(items)) return null;
        return new StationTable(sourceDate, items);
    }

    private static StationDoc? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StationDoc>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>用 StreamReader 显式按 UTF-8 读，避开 File.ReadAllText 在某些本机 locale 下的默认编码陷阱。</summary>
    private static string ReadUtf8(string path)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Station ToStation(StationRow r) =>
        new(r.Name, r.Telecode, r.Pinyin, r.ShortPinyin, r.CityCode, r.CityName);
}
