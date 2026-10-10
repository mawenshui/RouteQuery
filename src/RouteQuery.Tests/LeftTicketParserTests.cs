using System.Text.Json;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Tests;

/// <summary>
/// <see cref="LeftTicketParser"/> 的契约测试：用官方真实响应逐字段断言，而不是只断言"没抛异常"
/// （SPEC-005 二.4）。样本为 2026-10-09 实测的北京→保定，覆盖 K/T/Z/G 四种车型。
/// </summary>
public class LeftTicketParserTests
{
    private static LeftTicketParser NewParser() => new(Samples.Stations.Value);

    private static IReadOnlyList<TrainJourney> ParseCrossTypeSample()
    {
        using var doc = JsonDocument.Parse(Samples.Json("leftTicket-跨车型样本.json"));
        var data = doc.RootElement.GetProperty("data");

        var records = data.GetProperty("result").EnumerateArray()
            .Select(e => e.GetString()!).ToList();

        var names = new Dictionary<string, string>();
        foreach (var prop in data.GetProperty("map").EnumerateObject())
            names[prop.Name] = prop.Value.GetString()!;

        // 样本文件把 result 与 map 分两存放，这里重组为一份完整响应喂给解析器
        return NewParser().Parse(Samples.Response(records, names));
    }

    [Fact]
    public void 样本四条车次全部解析成功()
    {
        var journeys = ParseCrossTypeSample();
        Assert.NotEmpty(journeys);
        Assert.Contains("K599", journeys.Select(j => j.TrainCode));
        Assert.Contains("G6701", journeys.Select(j => j.TrainCode));
        Assert.Contains("Z295", journeys.Select(j => j.TrainCode));
        Assert.Contains("T145", journeys.Select(j => j.TrainCode));
    }

    [Fact]
    public void 时刻与历时按官方值解析()
    {
        var g6701 = ParseCrossTypeSample().Single(j => j.TrainCode == "G6701");

        Assert.Equal(new TimeSpan(5, 34, 0), g6701.Departure);
        Assert.Equal(new TimeSpan(6, 15, 0), g6701.Arrival);
        Assert.Equal(new TimeSpan(0, 41, 0), g6701.Duration);
        Assert.False(g6701.ArrivesNextDay);          // 41 分钟车程，不该被误标次日
        Assert.Equal("24000G67010K", g6701.TrainNo);
    }

    [Fact]
    public void 站名由码表落地且界面可显示全称()
    {
        var k599 = ParseCrossTypeSample().Single(j => j.TrainCode == "K599");

        Assert.Equal("FTP", k599.From.Telecode);
        Assert.Equal("北京丰台", k599.From.Name);   // 码表给出的全称，不是缩写
        Assert.Equal("保定", k599.To.Name);
    }

    [Fact]
    public void 票价按席别码解码并与官方接口一致()
    {
        var g6701 = ParseCrossTypeSample().Single(j => j.TrainCode == "G6701");

        // 官方 queryTicketPrice 对同一车次同一区间返回 174.0 / 79.0 / 49.0，实测逐项一致
        Assert.Equal(174.00m, Seat(g6701, SeatClass.BusinessClass)!.Price);
        Assert.Equal(79.00m, Seat(g6701, SeatClass.FirstClass)!.Price);
        Assert.Equal(49.00m, Seat(g6701, SeatClass.SecondClass)!.Price);
    }

    [Fact]
    public void 普速车次的硬座硬卧无座票价同样可解()
    {
        var k599 = ParseCrossTypeSample().Single(j => j.TrainCode == "K599");

        Assert.Equal(21.50m, Seat(k599, SeatClass.HardSeat)!.Price);
        Assert.Equal(102.50m, Seat(k599, SeatClass.HardSleeper)!.Price);
        Assert.Equal(67.50m, Seat(k599, SeatClass.SoftSleeper)!.Price);
    }

    [Fact]
    public void 放票时间解析为带时刻的时间点()
    {
        var g6701 = ParseCrossTypeSample().Single(j => j.TrainCode == "G6701");

        Assert.NotNull(g6701.OnSaleAt);
        Assert.Equal(2026, g6701.OnSaleAt!.Value.Year);
        Assert.Equal(9, g6701.OnSaleAt.Value.Month);
        Assert.Equal(28, g6701.OnSaleAt.Value.Day);
        Assert.Equal(8, g6701.OnSaleAt.Value.Hour);
    }

    [Fact]
    public void 无结果响应返回空集合而不是异常()
    {
        using var doc = JsonDocument.Parse(Samples.Json("leftTicket-无结果样本.json"));
        var body = doc.RootElement.GetProperty("原始响应").GetRawText();

        var journeys = NewParser().Parse(body);
        Assert.Empty(journeys);   // NoResult 是有效信息，必须由界面渲染空态（FR-10）
    }

    [Fact]
    public void 越界的HTML响应判为被拒绝而不是解析失败()
    {
        var file = Samples.Read("leftTicket-越界响应形态样本.txt");
        var body = file[file.IndexOf('<', StringComparison.Ordinal)..];   // 只取官方 HTML 正文

        var ex = Assert.Throws<QueryException>(() => NewParser().Parse(body));
        Assert.Equal(QueryErrorKind.UpstreamRejected, ex.Kind);
    }

    private static SeatAvailability? Seat(TrainJourney j, SeatClass c) => j.Seats.FirstOrDefault(s => s.Class == c);
}
