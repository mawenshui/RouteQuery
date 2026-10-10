using RouteQuery.Core.Ports;
using RouteQuery.Data.Store;

namespace RouteQuery.Tests;

/// <summary>
/// 收藏与历史的存储测试。逐条对着 FR-16 / FR-17 的验收标准写，
/// 因为这两条功能的验收点全是"重启后还在不在、顺序乱不乱、上限提示不提示"这类
/// 只有换一份实例才能证明的事。
/// </summary>
public class RouteBookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rq-book-{Guid.NewGuid():N}");
    private string Routes => Path.Combine(_dir, "routes.json");
    private string History => Path.Combine(_dir, "history.json");

    public RouteBookTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* 临时目录 */ }
    }

    private static SavedRoute Route(int i) => new($"线路{i}", "VNP", "AOH");

    [Fact]
    public void 收藏在换新实例后仍然存在()
    {
        var book = new JsonRouteBook(Routes);
        Assert.True(book.Add(Route(1)));
        Assert.True(book.Add(Route(2)));

        var reopened = new JsonRouteBook(Routes);     // 等价于重启应用
        Assert.Equal(["线路1", "线路2"], reopened.All.Select(r => r.Name));
    }

    [Fact]
    public void 删除一条不影响其余顺序()
    {
        var book = new JsonRouteBook(Routes);
        for (var i = 1; i <= 5; i++) book.Add(Route(i));

        Assert.True(book.Remove("线路3"));

        Assert.Equal(["线路1", "线路2", "线路4", "线路5"], book.All.Select(r => r.Name));
        Assert.False(book.Remove("不存在的名"));
    }

    [Fact]
    public void 达到上限时明确拒绝而不是静默丢弃()
    {
        var book = new JsonRouteBook(Routes);
        for (var i = 1; i <= IRouteBook.MaxEntries; i++)
            Assert.True(book.Add(Route(i)), $"第 {i} 条应当还能加");

        Assert.False(book.Add(Route(999)));                 // 调用方据此提示"收藏已满"
        Assert.Equal(IRouteBook.MaxEntries, book.All.Count);
    }

    [Fact]
    public void 同名收藏不被静默覆盖()
    {
        // 覆盖意味着用户以为存了两条不同线路，实际只剩一条——那是静默丢数据。
        var book = new JsonRouteBook(Routes);
        book.Add(new SavedRoute("回家", "VNP", "AOH"));

        Assert.False(book.Add(new SavedRoute("回家", "VNP", "SHH")));
        Assert.Single(book.All);
        Assert.Equal("AOH", book.All[0].ToTelecode);
    }

    [Fact]
    public void 收藏文件坏了就当没有而不是打不开应用()
    {
        File.WriteAllText(Routes, "{坏掉的 JSON");
        Assert.Empty(new JsonRouteBook(Routes).All);
    }

    [Fact]
    public void 历史按时间倒序且超上限淘汰最旧()
    {
        var history = new JsonQueryHistory(History);
        var day = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        for (var i = 1; i <= 35; i++)
        {
            history.Record(new QueryHistoryEntry("VNP", "AOH", new DateOnly(2026, 10, 1).AddDays(i), i, day.AddMinutes(i)));
        }

        Assert.Equal(IQueryHistory.MaxEntries, history.All.Count);
        Assert.Equal(35, history.All[0].ResultCount);           // 最近的在最前
        Assert.Equal(6, history.All[^1].ResultCount);           // 最旧的 5 条被淘汰
    }

    [Fact]
    public void 同程同日重复查询只占一条历史()
    {
        // 连查三次同一程不该把 30 条历史吃掉 3 条——那会让历史很快变成"今天的重复"。
        var history = new JsonQueryHistory(History);
        var date = new DateOnly(2026, 10, 20);

        history.Record(new QueryHistoryEntry("VNP", "AOH", date, 27, DateTimeOffset.Now));
        history.Record(new QueryHistoryEntry("VNP", "AOH", date, 28, DateTimeOffset.Now.AddSeconds(5)));

        Assert.Single(history.All);
        Assert.Equal(28, history.All[0].ResultCount);
    }

    [Fact]
    public void 清空历史要真的落到文件上()
    {
        var history = new JsonQueryHistory(History);
        history.Record(new QueryHistoryEntry("VNP", "AOH", new DateOnly(2026, 10, 20), 3, DateTimeOffset.Now));
        Assert.True(File.Exists(History));

        history.Clear();

        Assert.Empty(new JsonQueryHistory(History).All);        // 换实例后仍为空
        Assert.Equal("[]", File.ReadAllText(History).Trim());   // 文件层面确实移除了内容
    }
}
