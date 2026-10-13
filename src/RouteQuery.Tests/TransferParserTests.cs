using System.Globalization;
using System.Text.Json.Nodes;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.App.ViewModels;
using RouteQuery.Core.Ports;
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
        Assert.Equal(0, p.ArrivalDayOffset);
    }

    [Fact]
    public void 每段用的是这一段自己的乘车日期而不是整程那天()
    {
        // FR-15 展开该段经停时要把这个日期当 depart_date 发出去。第二段常常已经在中转站过夜，
        // 拿整程的 train_date 顶替就会取回另一天的时刻表——而它看起来完全正常（NFR-18）。
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        var legs = json["data"]!["middleList"]!.AsArray()[0]!["fullList"]!.AsArray();
        legs[1]!["start_train_date"] = "20261012";

        var p = Parse(json.ToJsonString()).Plans[0];

        Assert.Equal(new DateOnly(2026, 10, 11), p.First.TravelDate);
        Assert.Equal(new DateOnly(2026, 10, 12), p.Second.TravelDate);
        Assert.Equal(new DateOnly(2026, 10, 11), p.TravelDate);   // 整程日期仍然是官方给的那天，没被段内日期带跑
    }

    [Fact]
    public void 段内日期写成带横杠的判结构变更而不是宽松接受()
    {
        // 官方在段里用 yyyyMMdd、在方案层用 yyyy-MM-dd。用宽松解析会把两种写法都吃掉，
        // 于是哪天错了哪天没人知道——这里宁可直接失败。
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        json["data"]!["middleList"]!.AsArray()[0]!["fullList"]!.AsArray()[0]!["start_train_date"] = "2026-10-11";

        var ex = Assert.Throws<QueryException>(() => Parse(json.ToJsonString()));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("start_train_date", ex.Message);
    }

    [Fact]
    public void 段里缺日期时按形态异常丢弃该方案而不是补一个日期()
    {
        var json = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        json["data"]!["middleList"]!.AsArray()[0]!["fullList"]!.AsArray()[0]!.AsObject().Remove("start_train_date");

        var r = Parse(json.ToJsonString());

        Assert.Equal(9, r.Plans.Count);
        Assert.Equal(1, r.DroppedForShape);
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
    public void 界面时间必须是24小时制()
    {
        // 这一条是肉眼看出来的：G25 实际 17:00 出发，界面写成 05:00。
        // TimeOnly 的 "hh" 是 12 小时制且不带 AM/PM，把下午读成凌晨是会把人误上错车的错。
        var plan = Parse(Samples.Json(Sample)).Plans[0];
        var row = new TransferRowViewModel(plan, new KeyAsIsText());

        Assert.Equal("17:00 出发 · 21:18 到达", row.TimesText);
    }

    [Fact]
    public void 语义未确认的字段不得变成界面上的断言()
    {
        // same_station / isOutStation 在 10 份样本里恒为 "0"，而 same_train 是 "Y"（同一趟车继续坐，
        // 显然不需要换站）。含义没确定之前，界面不许说"需换车站/需出站"。
        var row = new TransferRowViewModel(Parse(Samples.Json(Sample)).Plans[0], new KeyAsIsText());

        Assert.DoesNotContain("中转_异站", row.TransferText);
        Assert.DoesNotContain("中转_需出站", row.TransferText);
        Assert.Contains("中转_换乘", row.TransferText);
    }

    /// <summary>只回键名本身：断言"用了哪条文案"而不是"文案内容对不对"，后者归界面走查。</summary>
    private sealed class KeyAsIsText : ITextProvider
    {
        public string Get(string key) => key;
    }

    [Fact]
    public void 时刻字段形态变了判解析失败而不是退化成凌晨零点()
    {
        // 旧写法是 TimeOnly.Parse 裸解：官方一旦把时刻写成空串或全角冒号，
        // 要么抛原始 FormatException（用户看到的是一句看不懂的话），要么解成 00:00（把人送到凌晨的站台）。
        var json = Mutate(list => list[0]!["fullList"]!.AsArray()[0]!["start_time"] = "17：00");

        var ex = Assert.Throws<QueryException>(() => Parse(json));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("start_time", ex.Message);
    }

    [Fact]
    public void 历时字段写成中文时判解析失败而不是当成零分钟()
    {
        // 这个漂移不是假设：同一个响应的 all_lishi 就是"4小时18分钟"这种中文，
        // 只有 lishi 是 hh:mm。官方哪天把两段也改成中文，我们必须停下来报错，不能显示"历时 0 分钟"。
        var json = Mutate(list => list[0]!["fullList"]!.AsArray()[0]!["lishi"] = "3小时14分");

        var ex = Assert.Throws<QueryException>(() => Parse(json));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("lishi", ex.Message);
    }

    [Fact]
    public void 数值字段解不出时不得当成0分钟()
    {
        // "候车 0分钟""全程 0分钟"是凭空造出来的数，而且长得像真数据（NFR-18）。
        var json = Mutate(list => list[0]!["wait_time_minutes"] = "无");

        var ex = Assert.Throws<QueryException>(() => Parse(json));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("wait_time_minutes", ex.Message);
    }

    [Theory]
    [InlineData(0, "")]              // 当天到：不标注
    [InlineData(1, "中转_次日")]       // 次日
    [InlineData(2, "中转_多日后")]     // 跨两天以上不许糊成"次日"
    public void 跨日标注必须跟官方给的两个日期相减结果一致(int dayOffset, string expectedKey)
    {
        var json = Mutate(list =>
            list[0]!["arrive_date"] = DateOnly.Parse(list[0]!["train_date"]!.GetValue<string>(), CultureInfo.InvariantCulture)
                .AddDays(dayOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var row = new TransferRowViewModel(Parse(json).Plans[0], new KeyAsIsText());

        Assert.Equal(expectedKey, row.ArrivalDayNote);
    }

    [Fact]
    public void 官方日期互相矛盾的方案丢弃并计入丢弃数()
    {
        // arrive_date 早于 train_date：第几天到达无从确定，宁可少一条并说明，也不能显示成"当天到"。
        var json = Mutate(list => list[0]!["arrive_date"] = "2020-01-01");

        var r = Parse(json);

        Assert.Equal(9, r.Plans.Count);
        Assert.Equal(1, r.DroppedForShape);
    }

    /// <summary>改样本的一处再整体解析：拿"官方改成这样我们会怎么表现"来验证失败路径。</summary>
    private static string Mutate(Action<JsonArray> mutate)
    {
        var root = JsonNode.Parse(Samples.Json(Sample))!.AsObject();
        mutate(root["data"]!["middleList"]!.AsArray());
        return root.ToJsonString();
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
