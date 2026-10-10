using RouteQuery.Core.Model;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Tests;

/// <summary>
/// 余票四态判定。核心风险来自实测发现的两件事：官方会把"无票"渲染成<b>候补</b>，
/// 而 <c>*</c> 根本不是余票值。把这两件当二态处理，"只看有票"就会筛出一堆买不到的车次（FR-11）。
/// </summary>
public class AvailabilityTriStateTests
{
    private static SeatState Of(string raw, PurchaseState p = PurchaseState.Buyable,
                                SeatClass seat = SeatClass.SecondClass, bool waitlist = false)
        => AvailabilityDecoder.Decode(raw, p, seat, waitlist);

    [Theory]
    [InlineData("有")]
    [InlineData("1")]
    [InlineData("20")]
    public void 有票的三种官方写法(string raw) => Assert.Equal(SeatState.Available, Of(raw));

    [Theory]
    [InlineData("")]
    [InlineData("无")]
    public void 无票的两种官方写法(string raw) => Assert.Equal(SeatState.SoldOut, Of(raw));

    [Fact]
    public void 无票加车次候补标记才成立候补()
    {
        Assert.Equal(SeatState.Waitlist, Of("无", waitlist: true));
        Assert.Equal(SeatState.SoldOut, Of("无", waitlist: false));   // 光有"无"字不能凭空造出候补
    }

    [Fact]
    public void 空值即使车次支持候补也不是候补()
    {
        // 官方判定式要求原始值恰为"无"。空意味着这个车次根本没有该席别。
        Assert.Equal(SeatState.SoldOut, Of("", waitlist: true));
    }

    [Theory]
    [InlineData(SeatClass.Standing)]
    [InlineData(SeatClass.Other)]
    public void 无座与其他永不标候补(SeatClass seat)
    {
        // 官方代码原文排除了 WZ_ 与 QT_。照抄这个条件，不做"顺手放宽"。
        Assert.Equal(SeatState.SoldOut, Of("无", seat: seat, waitlist: true));
    }

    [Fact]
    public void 尚未开售时一切席别都不是有票也不是无票()
    {
        foreach (var raw in new[] { "无", "*", "", "有" })
            Assert.Equal(SeatState.NotYetOnSale, Of(raw, PurchaseState.NotYetOnSale));
    }

    [Fact]
    public void 星号在已开售情形下判为未知而非无票()
    {
        // 星号只实测出现在未开售行。若它出现在已开售行，说明两列语义冲突，
        // 此时"不知道"是唯一诚实的答案（NFR-18）。
        Assert.Equal(SeatState.Unknown, Of("*"));
    }

    [Fact]
    public void 候补在只看有票的筛选下不出现()
    {
        var seats = new[]
        {
            new SeatAvailability(SeatClass.SecondClass, "有", SeatState.Available, 49.0m),
            new SeatAvailability(SeatClass.FirstClass, "无", SeatState.Waitlist, 79.0m),
        };
        var journey = JourneyWith(seats);

        Assert.True(journey.HasAnyTicket);                       // 因为有二等座
        var surviving = journey.Seats.Where(s => s.IsAvailable).ToList();
        Assert.Single(surviving);                                // 一等座可候补，但被筛掉
        Assert.Equal(SeatClass.SecondClass, surviving[0].Class);
    }

    [Fact]
    public void 全是候补的车次不算有票()
    {
        var journey = JourneyWith([
            new SeatAvailability(SeatClass.SecondClass, "无", SeatState.Waitlist, 49.0m)]);
        Assert.False(journey.HasAnyTicket);
    }

    [Fact]
    public void 未开售的车次不算有票也不算无票()
    {
        var journey = JourneyWith([
            new SeatAvailability(SeatClass.SecondClass, "*", SeatState.NotYetOnSale, null)]);
        Assert.False(journey.HasAnyTicket);
        Assert.Equal(49.00m, JourneyWith([
            new SeatAvailability(SeatClass.SecondClass, "有", SeatState.Available, 49.00m)]).LowestPrice);
    }

    private static TrainJourney JourneyWith(params SeatAvailability[] seats)
    {
        var st = new Station("保定", "BDP", "baoding", "bd", "0326", "保定");
        return new TrainJourney("G1", "240000G10L", st, st, st, st,
            new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0), new TimeSpan(1, 0, 0),
            false, PurchaseState.Buyable, seats, null);
    }
}
