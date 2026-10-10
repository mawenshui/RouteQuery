using System.Text.Json.Nodes;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Tests;

/// <summary>
/// 中转响应解析测试。样本是 2026-10-10 用真实登录态取的一次官方响应（<c>scretstr</c> 已脱敏）。
/// <para>这里最要紧的几条不是"能不能解出 10 条"，而是<b>解不出来时必须失败</b>、
/// <b>坏数据必须被丢掉并计数</b>、以及<b>票价一个都不许凭空出现</b>。</para>
/// </summary>
public class TransferParserTests
{
    private const string Sample = "lcQuery-中转样本.json";

    private static TransferParser.Result Parse(string json) => TransferParser.Parse(json, Samples.Stations.Value);

    [Fact]
    public void 真实样本解出十条方案且每条恰好两段()
    {
        var r = Parse(Samples.Json(Sample));

        Assert.Equal(10, r.Plans.Count);
        Assert.All(r.Plans, p =>
        {
            Assert.Equal(2, new[] { p.First.TrainNo, p.Second.TrainNo }.Length);
            Assert.NotEmpty(p.First.TrainCode);
            Assert.NotEmpty(p.Second.TrainCode);
        });
        Assert.Equal(0, r.DroppedForShape);
        Assert.Equal(0, r.DroppedForNegativeWait);
    }

    [Fact]
    public void 首条方案的每个显示字段都与官方原文逐格核对()
    {
        var p = Parse(Samples.Json(Sample)).Plans[0];

        Assert.Equal("G25", p.First.TrainCode);
        Assert.Equal("北京南", p.First.From.Name);
        Assert.Equal("南京南", p.First.To.Name);
        Assert.Equal(new TimeOnly(17, 0), p.First.Departure);
        Assert.Equal(new TimeOnly(20, 14), p.First.Arrival);
        Assert.Equal(TimeSpan.FromMinutes(194), p.First.Duration);

        Assert.Equal("南京南", p.MiddleStation.Name);
        Assert.Equal(2, p.WaitMinutes);
        Assert.Equal(TimeSpan.FromMinutes(2), p.WaitTime);
        // 中转站必须就是第一段的到达站，否则整个方案的物理含义都不成立
        Assert.Equal(p.First.To.Name, p.MiddleStation.Name);
        Assert.Equal(p.First.To.Telecode, p.MiddleStation.Telecode);

        Assert.Equal(258, p.TotalMinutes);
        Assert.Equal(TimeSpan.FromMinutes(258), p.TotalDuration);
        Assert.Equal("4小时18分钟", p.TotalDurationText);   // 官方原文，界面直接用它
        Assert.Equal(new DateOnly(2026, 10, 11), p.TravelDate);
        Assert.False(p.ArrivesNextDay);
    }

    [Fact]
    public void 官方没给票价所以一段都不许出现价格()
    {
        // 这个响应里根本没有 yp_info_new。任何一处出现非 null 的 Price 都说明我们在凭空造数。
        var r = Parse(Samples.Json(Sample));

        Assert.All(r.Plans, p =>
        {
            Assert.False(p.PricesKnown);
            Assert.All(p.First.Seats, s => Assert.Null(s.Price));
            Assert.All(p.Second.Seats, s => Assert.Null(s.Price));
        });
    }

    [Fact]
    public void 席别余票按官方原始值判定且候补一律不认()
    {
        var first = Parse(Samples.Json(Sample)).Plans[0].First;

        var second = Assert.Single(first.Seats.Where(s => s.Class == SeatClass.SecondClass));
        Assert.Equal("有", second.Raw);
        Assert.Equal(SeatState.Available, second.State);

        var hard = Assert.Single(first.Seats.Where(s => s.Class == SeatClass.HardSleeper));
        Assert.Equal("--", hard.Raw);
        // "--" 的语义官方从没说明过：可能是"该车不售这席别"，也可能是"无数据"。
        // 按 Unknown 透传而不是猜成无票——这是余票解码器既有的口径，中转这边不另开例外。
        Assert.Equal(SeatState.Unknown, hard.State);
    }

    [Fact]
    public void 负候车时长的方案被丢弃并计数()
    {
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        json["data"]!["middleList"]!.AsArray()[0]!["wait_time_minutes"] = -37;

        var r = Parse(json.ToJsonString());

        Assert.Equal(9, r.Plans.Count);
        Assert.Equal(1, r.DroppedForNegativeWait);
    }

    [Fact]
    public void 段数不是一段的方案按形态异常丢弃而不是硬凑()
    {
        // 三段方案是另一种产品形态，本项目只承诺两段。硬取前两段会给出一个官方没推荐过的组合。
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        var list = json["data"]!["middleList"]!.AsArray();
        list[0]!["fullList"] = new JsonArray(JsonNode.Parse(list[0]!["fullList"]!.AsArray()[0]!.ToJsonString()));

        var r = Parse(json.ToJsonString());

        Assert.Equal(9, r.Plans.Count);
        Assert.Equal(1, r.DroppedForShape);
    }

    [Fact]
    public void 方案缺关键字段时整批判解析失败而不是少显示一列()
    {
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        json["data"]!.AsObject().Remove("middleList");

        var ex = Assert.Throws<QueryException>(() => Parse(json.ToJsonString()));
        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
    }

    [Fact]
    public void 单条方案缺字段时按形态异常处理()
    {
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        json["data"]!["middleList"]!.AsArray()[0]!.AsObject().Remove("wait_time");

        var r = Parse(json.ToJsonString());
        Assert.Equal(9, r.Plans.Count);
        Assert.Equal(1, r.DroppedForShape);
    }

    [Fact]
    public void 不是JSON时判解析失败而不是崩在解析器里()
    {
        var ex = Assert.Throws<QueryException>(() => Parse("<html>登录超时</html>"));
        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
    }

    [Fact]
    public void 全部解不出时不得当成官方没有方案()
    {
        // 空数组是"官方说没有"（NoResult 语义）；有内容却一条都解不出是"我们解不动"（ParseFailure）。
        // 两者混起来会让用户在该等的时候改去试别的日期。
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        foreach (var item in json["data"]!["middleList"]!.AsArray().ToList())
            item!.AsObject().Remove("all_lishi");

        var ex = Assert.Throws<QueryException>(() => Parse(json.ToJsonString()));
        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
    }
}
