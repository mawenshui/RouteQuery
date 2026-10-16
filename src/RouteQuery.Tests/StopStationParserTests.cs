using System.Text.Json.Nodes;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Tests;

/// <summary>
/// <see cref="StopStationParser"/> 的契约测试。样本存的是"具名字段数组 + 元信息"，
/// 所以这里把站点数组重新包进官方那层 <c>data.data</c> 信封再喂给解析器——
/// 与 <see cref="Samples.Response"/> 给余票样本做的事一样：不硬编码响应文本，只重组结构。
/// </summary>
public class StopStationParserTests
{
    private const string Sample = "czxx-经停站样本.json";

    private static JsonArray Stops() =>
        JsonNode.Parse(Samples.Json(Sample))!["样本站点"]!.AsArray().DeepClone().AsArray();

    /// <summary>官方这一层的信封是 <c>data.data</c>（外层 data 里再套一个同名 data 才是站数组）。</summary>
    private static string BodyWith(JsonNode inner) =>
        new JsonObject
        {
            ["status"] = true,
            ["httpstatus"] = 200,
            ["data"] = new JsonObject { ["data"] = inner },
        }.ToJsonString();

    private static TrainRoute Parse(string body) => StopStationParser.Parse("330000K59800", body);

    [Fact]
    public void 样本里的站全部解出且首末标记正确()
    {
        var route = Parse(BodyWith(Stops()));

        Assert.Equal(4, route.Stops.Count);
        Assert.Equal("包头", route.Stops[0].StationName);
        Assert.True(route.Stops[0].IsFirst);
        Assert.False(route.Stops[0].IsLast);
        Assert.True(route.Stops[^1].IsLast);
        Assert.Equal("K598", route.DisplayTrainCode);
    }

    [Fact]
    public void 首站的到达时刻是占位符时留空而不是补成零点()
    {
        // 官方对始发站写 "----"。补成 00:00 会让"始发站当天 0 点到"出现在界面上，
        // 而那是会让人以为车已经来过的错。
        var route = Parse(BodyWith(Stops()));

        Assert.Null(route.Stops[0].ArriveTime);
        Assert.Equal(new TimeSpan(17, 6, 0), route.Stops[0].DepartureTime);
    }

    [Fact]
    public void data给成一段文字时判数据缺失而不是抛到界面外()
    {
        // 与中转、余票同一类隐患：官方把外层的 data 给成一段文字时，
        // 旧写法在 data.TryGetProperty 上抛 InvalidOperationException，界面拿不到任何降级话（2026-10-14 日志）。
        var ex = Assert.Throws<QueryException>(() => Parse(
            """{"status":true,"httpstatus":200,"data":"系统繁忙"}"""));

        Assert.Equal(QueryErrorKind.DataUnavailable, ex.Kind);
    }

    [Fact]
    public void 最外层不是对象时报结构变更而不是抛到界面外()
    {
        var ex = Assert.Throws<QueryException>(() => Parse("[1,2,3]"));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
    }

    [Fact]
    public void 站条目里混进非对象时跳过它而不是整批崩()
    {
        var stops = Stops();
        stops.Add(JsonValue.Create("占位"));

        var route = Parse(BodyWith(stops));

        Assert.Equal(4, route.Stops.Count);
    }

    [Fact]
    public void 一条站都没有时报数据缺失而不是显示空表()
    {
        var ex = Assert.Throws<QueryException>(() => Parse(BodyWith(new JsonArray())));

        Assert.Equal(QueryErrorKind.DataUnavailable, ex.Kind);
    }
}
