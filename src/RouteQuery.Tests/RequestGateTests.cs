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

    private static Task<int> Ok(CancellationToken ct) => Task.FromResult(1);

    [Fact]
    public async Task 没有动作窗口时任何请求都被拒绝()
    {
        var (gate, _) = NewGate();
        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Ok, default));
        Assert.Equal(RateLimitReason.ActionBudgetExceeded, ex.Reason);
    }

    [Fact]
    public async Task 基础动作第三次通过第四次被拒()
    {
        var (gate, _) = NewGate(basicBudget: 3);
        using var _w = gate.BeginAction(ActionKind.Basic);

        for (var i = 0; i < 3; i++) await gate.RunAsync(ActionKind.Basic, Ok, default);

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Ok, default));
        Assert.Equal(RateLimitReason.ActionBudgetExceeded, ex.Reason);
        Assert.Equal(3, gate.CurrentActionIssued);
    }

    [Fact]
    public async Task 用户连点时拒绝而不是排队()
    {
        // 这条测试守的是最容易"顺手改坏"的行为：改成排队体验更好，但节流就没了。
        var (gate, clock) = NewGate();

        using (var w = gate.BeginAction(ActionKind.Basic))
            await gate.RunAsync(ActionKind.Basic, Ok, default);

        using (var w2 = gate.BeginAction(ActionKind.Basic))
        {
            var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Ok, default));
            Assert.Equal(RateLimitReason.TooSoon, ex.Reason);
            Assert.Equal(0, clock.DelayCalls);   // 没有偷偷等待后代跑
        }
    }

    [Fact]
    public async Task 三秒之后允许下一次()
    {
        var (gate, clock) = NewGate();

        using (var w = gate.BeginAction(ActionKind.Basic))
            await gate.RunAsync(ActionKind.Basic, Ok, default);

        clock.Now = clock.Now + TimeSpan.FromSeconds(3.1);

        using var w2 = gate.BeginAction(ActionKind.Basic);
        await gate.RunAsync(ActionKind.Basic, Ok, default);   // 不应抛
    }

    [Fact]
    public async Task 扩展批次内部等待间隔而不是被拒()
    {
        var (gate, clock) = NewGate();
        using var _w = gate.BeginAction(ActionKind.Extension);

        await gate.RunAsync(ActionKind.Extension, Ok, default);   // 首个请求前没有历史，不必等
        await gate.RunAsync(ActionKind.Extension, Ok, default);   // 第二个必须等满间隔才发

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
            try { await gate.RunAsync(ActionKind.Extension, Ok, default); issued++; }
            catch (RateLimitedException) { break; }
        }

        Assert.Equal(12, issued);
    }

    [Fact]
    public async Task 当日额度用尽后一律拒绝且登录态阈值更低()
    {
        var (gate, _) = NewGate(dailyGuest: 2);
        using var _w = gate.BeginAction(ActionKind.Basic);

        await gate.RunAsync(ActionKind.Basic, Ok, default);
        await gate.RunAsync(ActionKind.Basic, Ok, default);

        Assert.True(gate.DailyBudgetExhausted);
        var ex = await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Ok, default));
        Assert.Equal(RateLimitReason.DailyBudgetExceeded, ex.Reason);
    }

    [Fact]
    public async Task 登录态当日额度更严()
    {
        var (gate, _) = NewGate(dailyGuest: 200);
        gate.SignedIn = true;
        gate.SeedDailyCount(100);

        Assert.True(gate.DailyBudgetExhausted);
        using var _w = gate.BeginAction(ActionKind.Basic);
        await Assert.ThrowsAsync<RateLimitedException>(() => gate.RunAsync(ActionKind.Basic, Ok, default));
    }

    [Fact]
    public async Task 并发请求被串行闸门挡下()
    {
        var (gate, _) = NewGate();
        using var _w = gate.BeginAction(ActionKind.Basic);
        var slow = new TaskCompletionSource<int>();

        var first = gate.RunAsync(ActionKind.Basic, _ => slow.Task, default);
        await Task.Delay(20);

        var ex = await Assert.ThrowsAsync<RateLimitedException>(
            () => gate.RunAsync(ActionKind.Basic, Ok, default));
        Assert.Equal(RateLimitReason.AlreadyRunning, ex.Reason);

        slow.SetResult(1);
        await first;
    }

    [Fact]
    public async Task 审计记录可用于发布前核对实际请求数()
    {
        var lines = new List<string>();
        var clock = new FakeClock();
        var gate = new RequestGate(clock, RequestGateOptions.Default, lines.Add);

        using (var _w = gate.BeginAction(ActionKind.Basic))
        {
            await gate.RunAsync(ActionKind.Basic, Ok, default);
            await gate.RunAsync(ActionKind.Basic, Ok, default);
        }

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Contains("Basic", l));
    }
}
