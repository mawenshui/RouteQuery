namespace RouteQuery.Data.Http;

/// <summary>
/// 闸门的落盘出口。
/// <para>为什么做成接口而不是让各调用点自己写日志：<b>"一次用户动作实际发了几个官方请求"是
/// SPEC-007 第四节的审计对象</b>，它必须与放行判定同源。散在业务代码里的日志会漏、会错，
/// 而且漏了没人知道——只有当写日志的那一行和记账的那一行在同一个类型里，
/// "日志里的条数 = 实际请求数"才是结构事实而不是约定。</para>
/// </summary>
public interface IGateSink
{
    /// <summary>一次官方请求被放行（即将发出）。<paramref name="dailyCount"/> 含本次在内的当日累计。</summary>
    void RequestIssued(ActionKind kind, string endpoint, int dailyCount, int remainingInAction);

    /// <summary>
    /// 请求失败。<paramref name="detail"/> 只允许是错误分类与短消息；
    /// 实现侧<b>必须</b>做截断与换行剥离（FR-23 要求日志内无完整请求头、无 Cookie）。
    /// </summary>
    void RequestFailed(ActionKind kind, string endpoint, string detail);

    /// <summary>与单次请求无直接关系但必须留痕的事件：会话清除、批次中止、日额度换日重置。</summary>
    void Note(string text);
}

/// <summary>空出口。测试与不需要日志的场合用，避免"可选回调为 null"的分支散落各处。</summary>
public sealed class NullGateSink : IGateSink
{
    public void RequestIssued(ActionKind kind, string endpoint, int dailyCount, int remainingInAction) { }
    public void RequestFailed(ActionKind kind, string endpoint, string detail) { }
    public void Note(string text) { }
}
