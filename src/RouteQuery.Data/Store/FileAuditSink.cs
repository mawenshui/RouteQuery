using System.Text;
using System.Text.Json;
using RouteQuery.Data.Http;

namespace RouteQuery.Data.Store;

/// <summary>当日计数的落盘形态。带日期，是为了让"重启后接着算"不至于把昨天的量算进今天。</summary>
public sealed record DailyCountFile(DateOnly Date, int Count);

/// <summary>
/// 闸门审计的本地落盘：一行一条请求事件 + 当日计数持久化。对应 FR-23 与 SPEC-007 四节的审计口径。
/// <para><b>写失败一律吞掉。</b>日志是辅助设施，它挂了不能连带把查询弄挂——亲友看到的会是
/// "这个工具一打开就报错"，而那比丢几行日志严重得多。</para>
/// <para>刻意<b>不</b>写的内容：Cookie、请求头、账号标识、响应正文。响应只记长度，
/// 因为"长度突然变了"足以提示列布局漂移，而正文里可能有用户输入的站名以外的东西。</para>
/// </summary>
public sealed class FileAuditSink(string? logDirectory = null, string? dailyCountPath = null, string appVersion = "未知版本") : IGateSink
{
    private readonly object _sync = new();
    private readonly string _dir = logDirectory ?? AppPaths.Logs;
    private readonly string _dailyPath = dailyCountPath ?? AppPaths.DailyCount;
    private readonly string _appVersion = appVersion;

    /// <summary>当日计数（含本次）与它属于哪一天。组合根在启动时读回来喂给闸门。</summary>
    public static DailyCountFile? ReadDailyCount(string? path = null)
    {
        var file = path ?? AppPaths.DailyCount;
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<DailyCountFile>(File.ReadAllText(file)) : null;
        }
        catch (Exception)
        {
            return null;   // 读不出来就当没有：宁可多给一次额度，也不让应用启动失败
        }
    }

    public void RequestIssued(ActionKind kind, string endpoint, int dailyCount, int remainingInAction)
    {
        Write($"{endpoint} {kind} 第{dailyCount}次发出（本动作剩余预算 {remainingInAction}）");
        SaveDailyCount(dailyCount);
    }

    public void RequestFailed(ActionKind kind, string endpoint, string detail) =>
        Write($"{endpoint} {kind} 失败：{Sanitize(detail)}");

    public void Note(string text) => Write($"事件：{Sanitize(text)}");

    private void SaveDailyCount(int count)
    {
        try
        {
            JsonFileStore.WriteAtomic(_dailyPath, JsonSerializer.Serialize(
                new DailyCountFile(DateOnly.FromDateTime(DateTime.Today), count)));
        }
        catch (Exception)
        {
            // 见类型注释：落盘失败不得影响查询。
        }
    }

    private void Write(string message)
    {
        try
        {
            File.AppendAllText(TodayLog(),
                $"{DateTime.Now:O} v{_appVersion} {Sanitize(message)}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception)
        {
            // 同上
        }
    }

    private string TodayLog() => Path.Combine(_dir, $"query-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>压成一行并截断。异常消息里可能带 URL，日志里留一行短摘要足够定位，不需要全文。</summary>
    private static string Sanitize(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 200 ? oneLine : oneLine[..200] + "…";
    }
}
