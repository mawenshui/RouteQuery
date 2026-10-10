using RouteQuery.Data.Http;
using RouteQuery.Data.Store;

namespace RouteQuery.Tests;

/// <summary>
/// 审计落盘测试。它守的不是"有没有写文件"，而是三件更容易出事的事：
/// ① 一行事件必须占一行（否则日志没法按行数对请求数）；
/// ② 异常消息里的换行与超长内容必须被压掉；
/// ③ 日志目录出问题时<b>不能连带把查询弄挂</b>。
/// </summary>
public class AuditSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rq-audit-{Guid.NewGuid():N}");

    public AuditSinkTests() => Directory.CreateDirectory(_dir);

    private string LogPath() => Path.Combine(_dir, "query.log");
    private string DailyPath() => Path.Combine(_dir, "daily.json");

    private FileAuditSink NewSink() => new(_dir, DailyPath(), "9.9.9-test");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* 临时目录清不掉不影响结论 */ }
    }

    [Fact]
    public void 一次放行写一行并带版本与当日序号()
    {
        var sink = NewSink();
        sink.RequestIssued(ActionKind.Basic, OfficialClient.ApiLeftTicket, 3, 0);

        var lines = File.ReadAllLines(Path.Combine(_dir, $"query-{DateTime.Now:yyyyMMdd}.log"));
        Assert.Single(lines);
        Assert.Contains("v9.9.9-test", lines[0]);
        Assert.Contains(OfficialClient.ApiLeftTicket, lines[0]);
        Assert.Contains("第3次", lines[0]);
        Assert.Contains("剩余预算 0", lines[0]);
    }

    [Fact]
    public void 异常消息里的换行与超长被压掉()
    {
        var sink = NewSink();
        sink.RequestFailed(ActionKind.Basic, "API-02 余票", "第一行\r\n第二行 " + new string('x', 400));

        var file = Path.Combine(_dir, $"query-{DateTime.Now:yyyyMMdd}.log");
        var lines = File.ReadAllLines(file);
        Assert.Single(lines);                       // 一条事件必须只占一行：换行被压成了空格
        Assert.Contains("第一行  第二行", lines[0]);
        Assert.EndsWith("…", lines[0]);             // 200 字以外被截掉
        Assert.True(lines[0].Length < 320, $"实际长度 {lines[0].Length}：截断没生效");
    }

    [Fact]
    public void 当日计数落盘后能读回并且带日期()
    {
        var sink = NewSink();
        sink.RequestIssued(ActionKind.Basic, "API-02 余票", 7, 1);

        var restored = FileAuditSink.ReadDailyCount(DailyPath());
        Assert.NotNull(restored);
        Assert.Equal(7, restored!.Count);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), restored.Date);
    }

    [Fact]
    public void 日志目录不可用时不影响调用方()
    {
        // 用一个"文件当目录用"的路径制造必然失败的写盘。
        var blocker = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocker, "我是文件不是目录");

        var sink = new FileAuditSink(Path.Combine(blocker, "sub"), Path.Combine(blocker, "daily.json"), "test");

        var ex = Record.Exception(() =>
        {
            sink.RequestIssued(ActionKind.Basic, "API-02 余票", 1, 2);
            sink.RequestFailed(ActionKind.Basic, "API-02 余票", "boom");
            sink.Note("事件");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void 损坏的当日计数文件被当作没有而不是崩溃()
    {
        File.WriteAllText(DailyPath(), "{这不是 JSON");
        Assert.Null(FileAuditSink.ReadDailyCount(DailyPath()));
    }
}
