using System.Text.Json;
using RouteQuery.Core.Errors;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Tests;

/// <summary>
/// 列布局漂移守卫。这是 RISK-01（官方改字段而应用静默给出错误数据）唯一的自动化防线，
/// 也是 SPEC-007 那条"自检不过一律整批不出数"规则的可执行证明。
/// </summary>
public class StructureDriftTests
{
    private static string BodyWith(params string[] records) =>
        Samples.Response(records, new Dictionary<string, string> { ["FTP"] = "北京丰台", ["BDP"] = "保定" });

    /// <summary>取一条真实记录（G6701），便于在其上做定向破坏。</summary>
    private static string RealRecord() => Samples.RecordOf("G6701");

    [Fact]
    public void 列数减少时整批判为解析失败()
    {
        var broken = string.Join('|', RealRecord().Split('|').SkipLast(1));   // 59 → 58
        var ex = Assert.Throws<QueryException>(() => new LeftTicketParser(Samples.Stations.Value).Parse(BodyWith(broken)));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("列数不符", ex.Detail);
    }

    [Fact]
    public void 上一版布局的真实样本现在必须判结构变更()
    {
        // 官方于 2026-10-28 把记录从 58 列改成 59 列（新列插在下标 57）。旧样本不删，
        // 它的角色变成"一个真实存在过的漂移反例"：拿它必须不出数并说"列数不符"，
        // 而不是按新表悄悄错位解析。这条比上面那条合成断言更值钱——
        // 因为 58 列不是我们编出来的坏输入，而是官方真的给过的那种形状。
        using var doc = JsonDocument.Parse(Samples.Json(Samples.CrossKindV3));
        var old = doc.RootElement.GetProperty("data").GetProperty("result")[0].GetString()!;
        Assert.Equal(58, old.Split('|').Length);

        var ex = Assert.Throws<QueryException>(
            () => new LeftTicketParser(Samples.Stations.Value).Parse(BodyWith(old)));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("列数不符", ex.Detail);
    }

    [Fact]
    public void 时刻列格式异常时判为解析失败而不是跳过该条()
    {
        var parts = RealRecord().Split('|');
        parts[8] = "0634";                                                   // 发车时刻变成无冒号
        var ex = Assert.Throws<QueryException>(() => new LeftTicketParser(Samples.Stations.Value).Parse(BodyWith(string.Join('|', parts))));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
        Assert.Contains("列 8", ex.Detail);
    }

    [Fact]
    public void 车次号列格式异常时判为解析失败()
    {
        var parts = RealRecord().Split('|');
        parts[3] = "??G5";
        Assert.Throws<QueryException>(() => new LeftTicketParser(Samples.Stations.Value).Parse(BodyWith(string.Join('|', parts))));
    }

    [Fact]
    public void 纯数字车次号必须通过自检()
    {
        // 回归用例：普客列车是纯数字车次（实测 1461）。
        // 早期设计把车次号校验写成 ^[GDCZTKLY]\d{1,4}$，会把整批普速列车判成结构异常，
        // 后果是"某个查询日期的普速全部消失"却没有任何报错——SPEC-007 v1.7 规则 1 纠正了它。
        // 断言走布局表的公开成员，测试里不复制正则，否则改了布局表而测试还绿着。
        var trainCode = LeftTicketColumnLayout.TrainCode;
        Assert.True(trainCode.Matches("1461"));
        Assert.True(trainCode.Matches("G531"));
        Assert.True(trainCode.Matches("Z295"));
        Assert.False(trainCode.Matches("G53106"));   // 五位以上仍应被拒
    }

    [Fact]
    public void 可购状态列不对取值施加格式校验()
    {
        // IS_TIME_NOT_BUY 是合法取值。若给这列加 ^[YN]$ 校验，所有尚未开售的车次会连带整批失败。
        Assert.False(LeftTicketColumnLayout.CanWebBuy.HasPattern);

        var parts = RealRecord().Split('|');
        parts[11] = "IS_TIME_NOT_BUY";
        var journeys = new LeftTicketParser(Samples.Stations.Value).Parse(BodyWith(string.Join('|', parts)));

        Assert.Single(journeys);
        Assert.Equal(Core.Model.PurchaseState.NotYetOnSale, journeys[0].PurchaseState);
    }

    [Fact]
    public void 空数组是正常的无结果而非结构错误()
    {
        var journeys = new LeftTicketParser(Samples.Stations.Value).Parse(
            """{"status":true,"httpstatus":200,"data":{"result":[],"map":{}}}""");
        Assert.Empty(journeys);
    }

    [Fact]
    public void data被官方给成一段文字时判数据缺失而不是抛到界面外()
    {
        // 2026-10-14 中转日志里那三条未处理异常就是这个形态（余票这一路是同一处隐患）。
        // 判 DataUnavailable 而不是 ParseFailure：前者是"等一会儿或去官网看"，
        // 后者是"这个版本查不了、去找新版"——把前者说成后者，用户会去要一个并不存在的新版本。
        var ex = Assert.Throws<QueryException>(() => new LeftTicketParser(Samples.Stations.Value).Parse(
            """{"status":true,"httpstatus":200,"data":"系统繁忙","messages":[]}"""));

        Assert.Equal(QueryErrorKind.DataUnavailable, ex.Kind);
    }

    [Fact]
    public void 最外层不是对象时报结构变更而不是抛到界面外()
    {
        // 合法 JSON 但不一定是对象：数组与裸字符串同样能走到取属性那一步。
        var ex = Assert.Throws<QueryException>(() => new LeftTicketParser(Samples.Stations.Value).Parse("[3,2,1]"));

        Assert.Equal(QueryErrorKind.ParseFailure, ex.Kind);
    }
}
