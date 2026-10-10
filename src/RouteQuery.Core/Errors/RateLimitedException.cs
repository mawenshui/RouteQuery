namespace RouteQuery.Core.Errors;

/// <summary>被本地节流闸门拦下的原因。刻意与 <see cref="QueryErrorKind"/> 分开：
/// 这些是<b>我们自己</b>拦的，不是官方拒绝的，文案必须说清楚"稍等几秒"而不是"官方不可用"。</summary>
public enum RateLimitReason
{
    /// <summary>距上次官方请求不足最小间隔（3 秒）。</summary>
    TooSoon,

    /// <summary>本次用户动作的请求数已达预算上限。</summary>
    ActionBudgetExceeded,

    /// <summary>当日累计请求数已达上限（登录态下阈值更严，因为风险落在账号上）。</summary>
    DailyBudgetExceeded,

    /// <summary>已有查询在途，本项目全程串行。</summary>
    AlreadyRunning,
}

/// <summary>
/// 本地节流拒绝。对应 FR-07 / NFR-17。
/// <para>刻意<b>不</b>继承 <see cref="QueryException"/>：把本地拦截混进"查询失败"会让用户以为
/// 是官方坏了，而那正是我们最不该制造的误解。</para>
/// </summary>
public sealed class RateLimitedException(RateLimitReason reason) : Exception
{
    public RateLimitReason Reason { get; } = reason;
}
