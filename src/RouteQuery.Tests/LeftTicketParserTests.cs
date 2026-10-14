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
    public void 官方运行图调整占位记录不再把整批判死()
    {
        // 2026-10-12 实测：北京南→上海虹桥 54 条记录里有 1 条是官方占位（车次 G547，
        // 时刻 24:00/24:00、历时 99:59、canWebBuy=IS_TIME_NOT_BUY、列 1 写着"列车运行图调整,暂停发售"）。
        // 它当时让整趟查询报"官方数据格式变了"，用户看到的是一个查不了的版本——而官方给了 53 趟真数据。
        var parser = NewParser();
        var journeys = parser.Parse(Samples.Json("leftTicket-运行图调整样本.json"));

        Assert.Equal(["1461", "G1", "G7"], journeys.Select(j => j.TrainCode).OrderBy(c => c).ToArray());
        Assert.DoesNotContain("G547", journeys.Select(j => j.TrainCode));
        Assert.Equal(["列车运行图调整,暂停发售"], parser.LastSuspendedMessages);
    }

    [Fact]
    public void 不像占位记录的时刻异常仍然判解析失败()
    {
        // 摘出去的条件是"哨兵值成套出现"。把历时改成正常值、时刻仍留 24:00，
        // 它就不该被当占位记录放过——自检必须响，否则这条规则会变成"以后什么怪时刻都放行"的口子。
        var tampered = RecordsOf("leftTicket-运行图调整样本.json");
        var placeholder = tampered.Single(r => r.Split('|')[3] == "G547");
        var columns = placeholder.Split('|');
        columns[10] = "06:00";
        tampered[tampered.IndexOf(placeholder)] = string.Join('|', columns);

        var ex = Assert.Throws<QueryException>(() => NewParser().Parse(Samples.Response(tampered)));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("列 8", ex.Message);
    }

    private static List<string> RecordsOf(string sample)
    {
        using var doc = JsonDocument.Parse(Samples.Json(sample));
        return doc.RootElement.GetProperty("data").GetProperty("result")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
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

        // 官方 queryTicketPrice 对同一车次同一区间返回 174.0 / 79.0 / 49.0，实测逐项一致。
        // 无座 49.0 来自尾巴 ≥3000 的那条 O 记录——官方自己也是这么认的（见 PriceDecoder）。
        Assert.Equal(174.00m, Seat(g6701, SeatClass.BusinessClass)!.Price);
        Assert.Equal(79.00m, Seat(g6701, SeatClass.FirstClass)!.Price);
        Assert.Equal(49.00m, Seat(g6701, SeatClass.SecondClass)!.Price);
        Assert.Equal(49.00m, Seat(g6701, SeatClass.Standing)!.Price);
    }

    [Fact]
    public void 普速车次的硬座硬卧无座票价同样可解()
    {
        var k599 = ParseCrossTypeSample().Single(j => j.TrainCode == "K599");

        // 短码 3=硬卧、4=软卧（官方 seatTypeCodeForName 原文）。旧表把这两个记反了，
        // 于是"硬卧 102.5 / 软卧 67.5"——一个比软卧还贵的硬卧，界面上一切看起来正常。
        // 判据不是我们信自己：K599 记录尾部的余票数 2/21 与 rw_num=2、yw_num=有 逐条对得上。
        Assert.Equal(21.50m, Seat(k599, SeatClass.HardSeat)!.Price);
        Assert.Equal(67.50m, Seat(k599, SeatClass.HardSleeper)!.Price);
        Assert.Equal(102.50m, Seat(k599, SeatClass.SoftSleeper)!.Price);
        Assert.Equal(21.50m, Seat(k599, SeatClass.Standing)!.Price);
    }

    [Fact]
    public void 每条票价记录尾部的余票数都落在它自己那一列上()
    {
        // 这是能把席别短码表整体问罪的守卫：一条记录说"某席别还剩 N 张"，
        // 那 N 必须等于官方给那个席别那一列的取值（>20 显示"有"、0 显示"无"）。
        // 记错短码时它对不上——2026-10-14 用旧表跑这条，22 条可比对里 11 条对不上。
        var checkedCount = 0;
        foreach (var record in RealRecords())
        {
            var cols = record.Split('|');
            var fares = cols[LeftTicketColumnLayout.FareString.Index];
            if (fares.Length % 10 != 0) continue;

            for (var i = 0; i < fares.Length / 10; i++)
            {
                var (code, tail) = (fares.Substring(i * 10, 1), int.Parse(fares[(i * 10 + 6)..(i * 10 + 10)]));
                var seatClass = tail >= 3000
                    ? SeatClass.Standing
                    : SeatCodeMap.SeatClassOf(code);
                if (seatClass is null) continue;
                var column = LeftTicketColumnLayout.AvailabilityColumns.First(c => c.Class == seatClass).Spec.Index;
                var official = cols[column];

                // 列里给的是给人看的话：数字、"有"（超过 20 张）、"无"（零张）。
                var expected = tail >= 3000 ? tail - 3000 : tail;
                var shown = expected switch
                {
                    0 => "无",
                    > 20 => "有",
                    _ => expected.ToString(),
                };
                Assert.True(official == shown,
                    $"{cols[3]} 短码 {code} 尾部 {tail} → {seatClass} 那一列实际是 \"{official}\"，不是 \"{shown}\"");
                checkedCount++;
            }
        }

        Assert.True(checkedCount >= 20, $"只有 {checkedCount} 条记录可比对，样本不足以证明对齐——检查样本是否被换掉");
    }

    /// <summary>两份真实样本里的全部竖线记录。对齐判据要的样本量不是单份样本能给的。</summary>
    private static IEnumerable<string> RealRecords()
    {
        var all = new List<string>();
        foreach (var file in new[] { "leftTicket-跨车型样本.json", "leftTicket-运行图调整样本.json" })
        {
            using var doc = JsonDocument.Parse(Samples.Json(file));
            all.AddRange(doc.RootElement.GetProperty("data").GetProperty("result")
                .EnumerateArray().Select(e => e.GetString()!));
        }
        return all;
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
