using RouteQuery.Core.Errors;
using RouteQuery.Data.Http;

namespace RouteQuery.Tests;

/// <summary>
/// 节流闸门测试。这个类型是"一次动作最多打官方几次"这条合规承诺的实现点，
/// 所以它的每一条数值都必须被钉住——包括"等待而不是排队"这个容易被误改的行为。
/// </summary>
public class RequestGateTests
{
    /// <summary>可控时钟：延时不真等，而是把时间往前推，从而能验证"间隔真的被强制了"。</summary>
    private sealed class FakeClock : IGateClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        public int DelayCalls { get; private set; }
        public TimeSpan LastDelay { get; private set; }

        public DateTimeOffset UtcNow => Now;

        public async Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            DelayCalls++;
            LastDelay = delay;
            Now = Now + delay;
            await Task.CompletedTask;
        }
    }

    private static (RequestGate gate, FakeClock clock) NewGate(
        int basicBudget = 3, int extensionBudget = 10, int extensionHardCap = 12,
        int dailyGuest = 200)
    {
        var clock = new FakeClock();
        var gate = new RequestGate(clock, new RequestGateOptions(
            TimeSpan.FromSeconds(3), basicBudget, extensionBudget, extensionHardCap, dailyGuest, 100));
        return (gate, clock);
    }

    /// <summary>日志用的接口标签。测试里用一个假标签，但它必须存在——
    /// 少一个参数就意味着"某条链路可以不写它是谁"。</summary>
    private const string Api = "API-99 测试";

    private static Task<int> Ok(CancellationToken ct) => Task.FromResult(1);

    [Fact]
    public async Task 没有动作窗口时任何请求都被拒绝()
    {
        var (gate, _) = NewGate();
        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
        Assert.Equal(RateLimitReason.ActionBudgetExceeded, ex.Reason);
    }

    [Fact]
    public async Task 基础动作第三次通过第四次被拒()
    {
        var (gate, _) = NewGate(basicBudget: 3);
        using var _w = gate.BeginAction(ActionKind.Basic);

        for (var i = 0; i < 3; i++) await gate.RunAsync(ActionKind.Basic, Api, Ok, default);

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
        Assert.Equal(RateLimitReason.ActionBudgetExceeded, ex.Reason);
        Assert.Equal(3, gate.CurrentActionIssued);
    }

    [Fact]
    public async Task 用户连点时拒绝而不是排队()
    {
        // 这条测试守的是最容易"顺手改坏"的行为：改成排队体验更好，但节流就没了。
        var (gate, clock) = NewGate();

        using (var w = gate.BeginAction(ActionKind.Basic))
            await gate.RunAsync(ActionKind.Basic, Api, Ok, default);

        using (var w2 = gate.BeginAction(ActionKind.Basic))
        {
            var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
            Assert.Equal(RateLimitReason.TooSoon, ex.Reason);
            Assert.Equal(0, clock.DelayCalls);   // 没有偷偷等待后代跑
        }
    }

    [Fact]
    public async Task 三秒之后允许下一次()
    {
        var (gate, clock) = NewGate();

        using (var w = gate.BeginAction(ActionKind.Basic))
            await gate.RunAsync(ActionKind.Basic, Api, Ok, default);

        clock.Now = clock.Now + TimeSpan.FromSeconds(3.1);

        using var w2 = gate.BeginAction(ActionKind.Basic);
        await gate.RunAsync(ActionKind.Basic, Api, Ok, default);   // 不应抛
    }

    [Fact]
    public async Task 扩展批次内部等待间隔而不是被拒()
    {
        var (gate, clock) = NewGate();
        using var _w = gate.BeginAction(ActionKind.Extension);

        await gate.RunAsync(ActionKind.Extension, Api, Ok, default);   // 首个请求前没有历史，不必等
        await gate.RunAsync(ActionKind.Extension, Api, Ok, default);   // 第二个必须等满间隔才发

        Assert.Equal(1, clock.DelayCalls);
        Assert.True(clock.LastDelay >= TimeSpan.FromSeconds(3));
        Assert.Equal(2, gate.CurrentActionIssued);
    }

    [Fact]
    public async Task 扩展预算配置再大也不越硬上限()
    {
        // FR-29 验收③：把设置改成 99，实际请求数仍然不能超过硬上限 12。
        var (gate, _) = NewGate(extensionBudget: 10, extensionHardCap: 12);
        using var _w = gate.BeginAction(ActionKind.Extension, budgetOverride: 99);

        var issued = 0;
        for (var i = 0; i < 30; i++)
        {
            try { await gate.RunAsync(ActionKind.Extension, Api, Ok, default); issued++; }
            catch (RateLimitedException) { break; }
        }

        Assert.Equal(12, issued);
    }

    [Fact]
    public async Task 当日额度用尽后一律拒绝且登录态阈值更低()
    {
        var (gate, _) = NewGate(dailyGuest: 2);
        using var _w = gate.BeginAction(ActionKind.Basic);

        await gate.RunAsync(ActionKind.Basic, Api, Ok, default);
        await gate.RunAsync(ActionKind.Basic, Api, Ok, default);

        Assert.True(gate.DailyBudgetExhausted);
        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
        Assert.Equal(RateLimitReason.DailyBudgetExceeded, ex.Reason);
    }

    [Fact]
    public async Task 登录态当日额度更严()
    {
        var (gate, _) = NewGate(dailyGuest: 200);
        gate.SignedIn = true;
        gate.SeedDailyCount(100, DateOnly.FromDateTime(new DateTime(2026, 10, 10)));

        Assert.True(gate.DailyBudgetExhausted);
        using var _w = gate.BeginAction(ActionKind.Basic);
        await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
    }

    [Fact]
    public async Task 并发请求被串行闸门挡下()
    {
        var (gate, _) = NewGate();
        using var _w = gate.BeginAction(ActionKind.Basic);
        var slow = new TaskCompletionSource<int>();

        var first = gate.RunAsync(ActionKind.Basic, Api, _ => slow.Task, default);
        await Task.Delay(20);

        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            () => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
        Assert.Equal(RateLimitReason.AlreadyRunning, ex.Reason);

        slow.SetResult(1);
        await first;
    }

    [Fact]
    public async Task 审计记录可用于发布前核对实际请求数()
    {
        var sink = new RecordingSink();
        var clock = new FakeClock();
        var gate = new RequestGate(clock, RequestGateOptions.Default, sink);

        using (var _w = gate.BeginAction(ActionKind.Basic))
        {
            await gate.RunAsync(ActionKind.Basic, Api, Ok, default);
            await gate.RunAsync(ActionKind.Basic, Api, Ok, default);
        }

        Assert.Equal(2, sink.Issued.Count);
        Assert.All(sink.Issued, r => Assert.Equal(ActionKind.Basic, r.Kind));
        // 当日累计逐条递增、剩余预算逐条递减——发布前就是拿这两列数字对抓包结果。
        Assert.Equal([1, 2], sink.Issued.Select(r => r.DailyCount));
        Assert.Equal([2, 1], sink.Issued.Select(r => r.RemainingInAction));
    }

    [Fact]
    public async Task 请求失败也要留一行痕()
    {
        // 亲友说"查不出来"时，日志必须能区分"官方拒了"和"我们解析错了"。
        var sink = new RecordingSink();
        var gate = new RequestGate(new FakeClock(), RequestGateOptions.Default, sink);

        using var _w = gate.BeginAction(ActionKind.Basic);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync(ActionKind.Basic, Api, _ => throw new InvalidOperationException("boom"), default));

        Assert.Single(sink.Issued);
        Assert.Single(sink.Failures);
        Assert.Equal(ActionKind.Basic, sink.Failures[0].Kind);
        Assert.Contains("boom", sink.Failures[0].Detail);
    }

    [Fact]
    public async Task 换日之后当日计数归零()
    {
        var (gate, clock) = NewGate(dailyGuest: 2);
        using var _w = gate.BeginAction(ActionKind.Basic);

        await gate.RunAsync(ActionKind.Basic, Api, Ok, default);
        await gate.RunAsync(ActionKind.Basic, Api, Ok, default);
        Assert.True(gate.DailyBudgetExhausted);

        clock.Now = clock.Now + TimeSpan.FromDays(1);
        await gate.RunAsync(ActionKind.Basic, Api, Ok, default);   // 新的一天，额度回来了

        Assert.Equal(1, gate.DailyCount);
        Assert.NotEqual(gate.DailyDate, DateOnly.FromDateTime(clock.Now.AddDays(-1).Date));
    }

    [Fact]
    public async Task 过期日期的恢复值不被采用()
    {
        // 昨天的计数不能吃今天的额度；但同日的值必须能恢复，否则重启就绕过日额度。
        var (gate, clock) = NewGate(dailyGuest: 2);
        var today = DateOnly.FromDateTime(clock.Now.Date);

        gate.SeedDailyCount(2, today.AddDays(-1));
        Assert.False(gate.DailyBudgetExhausted);

        gate.SeedDailyCount(2, today);
        Assert.True(gate.DailyBudgetExhausted);
        using var _w = gate.BeginAction(ActionKind.Basic);
        await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Api, Ok, default));
    }

    /// <summary>记录型出口：审计行为只有能被断言，才会在被改坏时被测试抓住。</summary>
    private sealed class RecordingSink : IGateSink
    {
        public List<(ActionKind Kind, string Endpoint, int DailyCount, int RemainingInAction)> Issued { get; } = [];
        public List<(ActionKind Kind, string Endpoint, string Detail)> Failures { get; } = [];
        public List<string> Notes { get; } = [];

        public void RequestIssued(ActionKind kind, string endpoint, int dailyCount, int remainingInAction) =>
            Issued.Add((kind, endpoint, dailyCount, remainingInAction));

        public void RequestFailed(ActionKind kind, string endpoint, string detail) =>
            Failures.Add((kind, endpoint, detail));

        public void Note(string text) => Notes.Add(text);
    }
}

