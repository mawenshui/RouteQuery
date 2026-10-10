using System.Text.Json;
using RouteQuery.Core.Model;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Tests;

/// <summary>
/// 列语义来源的守卫。它把 SPEC-007 那条"未确认的列不得参与判断"从口头约定变成可执行检查——
/// 之所以值得写，是因为本项目已经两次把"看起来合理"当成了"已确认"（mapper 与 yp_ex）。
/// </summary>
public class ColumnProvenanceTests
{
    [Fact]
    public void 所有参与判断的列都必须由官方脚本书证()
    {
        var judged = LeftTicketColumnLayout.AvailabilityColumns
            .Where(x => x.Spec.Provenance != ColumnProvenance.ConfirmedByOfficialScript)
            .Select(x => x.Class)
            .ToList();

        // 三列中文标签未确认，处于 Unverified：它们不会进入筛选与排序依赖的席别集合。
        Assert.Equal([SeatClass.Unknown20, SeatClass.Unknown27, SeatClass.Unknown33], judged);
    }

    [Fact]
    public void 候选生成所依赖的票价列必须是已确认来源()
    {
        Assert.Equal(ColumnProvenance.ConfirmedByOfficialScript, LeftTicketColumnLayout.FareString.Provenance);
        Assert.Equal(ColumnProvenance.ConfirmedByOfficialScript, LeftTicketColumnLayout.CanWebBuy.Provenance);
        Assert.Equal(ColumnProvenance.ConfirmedByOfficialScript, LeftTicketColumnLayout.HoubuTrainFlag.Provenance);
    }

    [Fact]
    public void 改动yp_ex列不影响任何解析结果()
    {
        // yp_ex（列 34）曾被误判为余票列。这条测试是它"确实没有被接入判断"的证明：
        // 把它整列换成别的内容，解析出来的车次应当一模一样。
        var record = RealRecord();

        var baseline = new LeftTicketParser(Samples.Stations.Value).Parse(Body(record));

        var tamperedParts = record.Split('|');
        tamperedParts[34] = "完全不相干的内容";
        var tampered = new LeftTicketParser(Samples.Stations.Value).Parse(Body(string.Join('|', tamperedParts)));

        Assert.Equal(baseline.Count, tampered.Count);
        Assert.Equal(baseline[0].Seats.Select(s => s.State), tampered[0].Seats.Select(s => s.State));
        Assert.Equal(baseline[0].Seats.Select(s => s.Price), tampered[0].Seats.Select(s => s.Price));
        Assert.Equal(baseline[0].HasAnyTicket, tampered[0].HasAnyTicket);
    }

    [Fact]
    public void 布局版本与期望列数被显式记录()
    {
        // 这两个值是排查"某天开始查不出东西"时的第一对照点，因此要被测试钉住：
        // 有人顺手改了它们时必须有人来解释为什么。
        Assert.Equal(58, LeftTicketColumnLayout.ExpectedColumnCount);
        Assert.True(LeftTicketColumnLayout.LayoutVersion >= 3);
    }

    private static string RealRecord() => Samples.RecordOf("G6701");

    private static string Body(string record) =>
        Samples.Response([record], new Dictionary<string, string> { ["BXP"] = "北京西", ["BMP"] = "保定东" });
}
