using RouteQuery.Core.Errors;

namespace RouteQuery.Data.Http;

/// <summary>动作类型。请求预算按类型分级——这是"多买几站"能与频率红线共存的前提。</summary>
public enum ActionKind
{
    /// <summary>基础查询：一次查询动作（直达 + 会话初始化）。</summary>
    Basic,

    /// <summary>区间扩展：一次"多买几站"动作，逐候选串行查询。</summary>
    Extension,

    /// <summary>经停站等钻取查询，单独计，避免与列表查询互相挤占。</summary>
    Drilldown,
}

/// <summary>闸门参数。数值一律来自 SPEC-007 第四节，改数值要先改规范。</summary>
/// <param name="MinInterval">相邻两次官方请求的最小间隔。</param>
/// <param name="BasicBudget">基础动作的单动作预算。</param>
/// <param name="ExtensionBudget">扩展动作的默认预算。</param>
/// <param name="ExtensionHardCap">扩展动作的硬上限，配置不得越过。</param>
/// <param name="DailyCapGuest">游客态当日总额。</param>
/// <param name="DailyCapSignedIn">登录态当日总额——刻意更低，因为风险落在亲友账号上。</param>
public sealed record RequestGateOptions(
    TimeSpan MinInterval,
    int BasicBudget = 3,
    int ExtensionBudget = 10,
    int ExtensionHardCap = 12,
    int DailyCapGuest = 200,
    int DailyCapSignedIn = 100)
{
    public static RequestGateOptions Default { get; } = new(TimeSpan.FromSeconds(3));
}

/// <summary>
/// 官方请求的唯一出口。对应 FR-07 / NFR-08 / NFR-17。
/// <para>做成单点而不是各处 <c>Task.Delay</c>：散点延时会被"再加一个功能"逐个绕过，
/// 集中闸门让 SPEC-007 那张频率表可以用一个类型审计。</para>
/// <para><b>间隔的两种处理是刻意的区别</b>：用户主动点击（Basic / Drilldown）不足间隔时<b>拒绝</b>，
/// 否则连点十次会攒出十个请求依次打出去，那是"看起来有节流、实际是队列"的反例；
/// 扩展批次内部（Extension）则<b>等待</b>到间隔满足再打下一个候选，否则功能直接不可用。</para>
/// </summary>
public sealed class RequestGate(IGateClock clock, RequestGateOptions options, IGateSink? sink = null)
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly object _sync = new();

    private DateTimeOffset? _plannedCallAt;   // 上一次调用"实际发出"的时刻（等待后）
    private ActionWindow? _window;
    private int _dailyCount;
    private DateOnly _dailyDate = DateOnly.FromDateTime(clock.UtcNow.Date);

    /// <summary>当日已发出的官方请求数。</summary>
    public int DailyCount { get { lock (_sync) return _dailyCount; } }

    /// <summary>当日计数属于哪一天。落盘时一起写，换日恢复时用来判断该不该沿用。</summary>
    public DateOnly DailyDate { get { lock (_sync) return _dailyDate; } }

    /// <summary>是否登录态，影响当日额度。</summary>
    public bool SignedIn { get; set; }

    /// <summary>当日额度是否已用尽。</summary>
    public bool DailyBudgetExhausted { get { lock (_sync) return _dailyCount >= DailyCap; } }

    /// <summary>给上层补一条事件（批次中止、会话清除等）。审计的意义在于"谁都能补一行"，
    /// 但补的位置必须仍然与记账同侧，否则日志又会变成各写各的。</summary>
    public void Note(string text) => sink?.Note(text);

    /// <summary>当前动作已发出的请求数，批次结束后供界面与日志核对。</summary>
    public int CurrentActionIssued { get { lock (_sync) return _window?.Issued ?? 0; } }

    private int DailyCap => SignedIn ? options.DailyCapSignedIn : options.DailyCapGuest;

    /// <summary>
    /// 开始一次用户动作的计数窗口。<b>所有官方调用都必须落在某个窗口内</b>，
    /// 没有窗口时 <see cref="RunAsync"/> 直接拒绝——这条约束使"绕过闸门"在结构上不可能。
    /// </summary>
    /// <param name="budgetOverride">仅对 Extension 生效，且始终被钳制在硬上限内：把配置改成 99 也拿不到更多请求。</param>
    public IDisposable BeginAction(ActionKind kind, int? budgetOverride = null)
    {
        lock (_sync)
        {
            _window = new ActionWindow(kind, BudgetFor(kind, budgetOverride), kind == ActionKind.Extension);
            return new Scope(this);
        }
    }

    /// <summary>当日计数的跨启动恢复。日期不匹配时不采用——过期的计数会把今天的额度吃掉。</summary>
    public void SeedDailyCount(int count, DateOnly forDate)
    {
        lock (_sync)
        {
            if (forDate != DateOnly.FromDateTime(clock.UtcNow.Date)) return;
            _dailyCount = count;
            _dailyDate = forDate;
        }
    }

    /// <summary>
    /// 在闸门保护下执行一次官方调用。<b>不发请求</b>时抛 <see cref="RateLimitedException"/>。
    /// <para><paramref name="endpoint"/> 是给人看的接口编号标签（如 <c>API-02 余票</c>），
    /// 只进日志不进界面——SPEC-007 第九节规定 <c>API-xx</c> 只存在于文档与日志里。</para>
    /// </summary>
    public async Task<T> RunAsync<T>(ActionKind kind, string endpoint, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (!await _serial.WaitAsync(0, ct))
            throw new RateLimitedException(RateLimitReason.AlreadyRunning);

        try
        {
            var wait = Reserve(kind, endpoint);
            if (wait > TimeSpan.Zero)
                await clock.DelayAsync(wait, ct);

            try
            {
                return await work(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 失败也要留痕：亲友反馈"查不出来"时，日志里必须能看到是官方拒了还是我们解析错了。
                sink?.RequestFailed(kind, endpoint, $"{ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
        finally
        {
            _serial.Release();
        }
    }

    /// <summary>无返回值的场合（如建立会话）。与泛型版共用同一套记账，避免"没有返回值就不走闸门"的漏洞。</summary>
    public Task RunAsync(ActionKind kind, string endpoint, Func<CancellationToken, Task> work, CancellationToken ct) =>
        RunAsync<object?>(kind, endpoint, async t => { await work(t).ConfigureAwait(false); return null; }, ct);

    /// <summary>记账并返回需要等待的时长（0 表示不必等）。该拒绝时在这里抛。</summary>
    private TimeSpan Reserve(ActionKind kind, string endpoint)
    {
        lock (_sync)
        {
            RollDateIfNeeded();

            if (_dailyCount >= DailyCap)
                throw new RateLimitedException(RateLimitReason.DailyBudgetExceeded);

            if (_window is not { } window || window.Kind != kind)
                throw new RateLimitedException(RateLimitReason.ActionBudgetExceeded);

            if (window.Issued >= window.Budget)
                throw new RateLimitedException(RateLimitReason.ActionBudgetExceeded);

            var wait = TimeSpan.Zero;

            if (window.WaitsForInterval)
            {
                // 扩展批次：每一次候选调用之前都要等满间隔。
                if (_plannedCallAt is { } last)
                {
                    var remaining = options.MinInterval - (clock.UtcNow - last);
                    if (remaining > TimeSpan.Zero) wait = remaining;
                }
            }
            else if (window.Issued == 0)
            {
                // 用户主动查询：只在<b>动作边界</b>上检查间隔，不足则拒绝而不是排队。
                // 刻意不在动作内部检查——"会话初始化 + 取数"本来就是连着发的两次请求，
                // 官方页面自己也是这么做的；若把它们之间也卡 3 秒，应用将无法工作。
                if (_plannedCallAt is { } last)
                {
                    var since = clock.UtcNow - last;
                    if (since < options.MinInterval)
                        throw new RateLimitedException(RateLimitReason.TooSoon);
                }
            }

            _window = window with { Issued = window.Issued + 1 };
            _dailyCount++;
            _plannedCallAt = clock.UtcNow + wait;   // 记的是"实际发出"的时刻，不是"决定要发"的时刻
            sink?.RequestIssued(kind, endpoint, _dailyCount, window.Budget - window.Issued - 1);
            return wait;
        }
    }

    /// <summary>
    /// 换日归零。应用长期开着跨过一次午夜时，昨天的计数不该继续吃今天的额度；
    /// 反过来，如果日志里出现"当日计数从大值突然变 0"，那就是换日而不是丢日志。
    /// </summary>
    private void RollDateIfNeeded()
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.Date);
        if (today == _dailyDate) return;

        _dailyDate = today;
        _dailyCount = 0;
        sink?.Note($"换日，当日计数归零：{today:yyyy-MM-dd}");
    }

    private int BudgetFor(ActionKind kind, int? budgetOverride) => kind switch
    {
        ActionKind.Extension => Math.Clamp(budgetOverride ?? options.ExtensionBudget, 1, options.ExtensionHardCap),
        ActionKind.Basic => options.BasicBudget,
        _ => 1,
    };

    private sealed record ActionWindow(ActionKind Kind, int Budget, bool WaitsForInterval)
    {
        public int Issued { get; init; }
    }

    private sealed class Scope(RequestGate gate) : IDisposable
    {
        public void Dispose()
        {
            lock (gate._sync) gate._window = null;
        }
    }
}
