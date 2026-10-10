using RouteQuery.Core.Model;
using RouteQuery.Data.Stations;

namespace RouteQuery.Tests;

/// <summary>站点模糊匹配。对应 FR-01 的四条验收标准，直接跑 3404 条真码表。</summary>
public class StationIndexTests
{
    private static StationIndex Index => Samples.Stations.Value;

    [Fact]
    public void 码表规模与官方一致()
    {
        Assert.True(Index.IsLoaded);
        Assert.Equal(3404, Index.Count);
    }

    [Fact]
    public void 输入北京至少出现四个北京地区车站()
    {
        var hits = Index.Search("北京");
        Assert.True(hits.Count >= 4, $"只命中 {hits.Count} 条");
        Assert.Contains(hits, s => s.Name == "北京");
        Assert.Contains(hits, s => s.Name == "北京南");
        Assert.Contains(hits, s => s.Name == "北京西");
    }

    [Fact]
    public void 同城多站绝不合并成一条()
    {
        var hits = Index.Search("北京").Where(s => s.CityCode == "0357").ToList();
        Assert.True(hits.Count > 1);
        Assert.Equal(hits.Count, hits.Select(h => h.Telecode).Distinct().Count());  // 每条都是独立车站
    }

    [Fact]
    public void 简拼命中具体车站()
    {
        var hits = Index.Search("bjn");
        Assert.Contains("北京南", hits.Select(s => s.Name));
    }

    [Fact]
    public void 包含匹配也能命中()
    {
        Assert.Contains("上海虹桥", Index.Search("虹桥").Select(s => s.Name));
    }

    [Fact]
    public void 精确名排在最前()
    {
        Assert.Equal("上海", Index.Search("上海").First().Name);
    }

    [Fact]
    public void 未收录的输入返回空并由界面给引导文案()
    {
        Assert.Empty(Index.Search("zzz不存在的站"));
    }

    [Fact]
    public void 空白输入不返回全部车站()
    {
        Assert.Empty(Index.Search("   "));
        Assert.Empty(Index.Search(""));
    }

    [Fact]
    public void 三字码可反查且大小写敏感于官方码()
    {
        Assert.Equal("北京南", Index.FindByTelecode("VNP")?.Name);
        Assert.Null(Index.FindByTelecode("XXX"));
    }

    [Fact]
    public void 同城聚合依赖码表的城市代码字段()
    {
        var beijing = Index.FindByTelecode("VNP")!;
        var same = Index.SameCityAs(beijing);
        Assert.Contains(same, s => s.Name == "北京");
        Assert.DoesNotContain(same, s => s.Name == "保定");
    }

    [Fact]
    public void 码表规模校验能拦住被截断的更新()
    {
        var tiny = string.Concat(Enumerable.Repeat("@aaa|某站A|A01|a|a|0|0001|某市|||", 10));
        Assert.Null(StationNameLoader.ParseFromOfficial("var station_names ='" + tiny + "'", new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void 官方码表解析取的是第一位为键之后的字段()
    {
        // 回归：官方格式首位是检索键（bjb），站名在第二位。把首位当站名会整表错位。
        // 这里刻意用 ParseEntries 而不是 ParseFromOfficial——后者带"条目数不少于 3000"的
        // 完整性闸门，两行夹具本来就该被它拒绝（上一条测试测的就是这个）。
        var items = StationNameLoader.ParseEntries(
            "@bjn|北京南|VNP|beijingnan|bjn|3|0357|北京|||@bdp|保定|BDP|baoding|bd|216|0326|保定|||");

        Assert.Equal(2, items.Count);
        Assert.Equal("北京南", items[0].Name);
        Assert.Equal("VNP", items[0].Telecode);
        Assert.Equal("beijingnan", items[0].Pinyin);
        Assert.Equal("0357", items[0].CityCode);
        Assert.Equal("北京", items[0].CityName);
        Assert.Equal("BDP", items[1].Telecode);
    }
}
