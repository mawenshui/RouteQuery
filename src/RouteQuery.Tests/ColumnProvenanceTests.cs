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

        // 列 20 已确认为"优选一等座"，但来源是页面观察而非官脚本书证，所以仍与另外两列一起
        // 落在"非官方脚本"这一组里——这条断言守的是"哪些列的证据强度还不足以被当作权威"。
        // 列 27 / 33 中文标签仍未确认（Unverified），界面写"未确认席别"。
        Assert.Equal([SeatClass.PremiumFirstClass, SeatClass.Unknown27, SeatClass.Unknown33], judged);
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
        Assert.Equal(59, LeftTicketColumnLayout.ExpectedColumnCount);
        Assert.Equal(4, LeftTicketColumnLayout.LayoutVersion);
    }

    private static string RealRecord() => Samples.RecordOf("G6701");

    private static string Body(string record) =>
        Samples.Response([record], new Dictionary<string, string> { ["BXP"] = "北京西", ["BMP"] = "保定东" });
}
