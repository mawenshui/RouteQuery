using System.Net;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;
using RouteQuery.Data.Services;

namespace RouteQuery.Tests;

/// <summary>
/// 风控与会话失效这两条路径。它们以前记在验收报告的"未自动化清单"里，原话是
/// "需要能注入响应的假客户端，而 <c>OfficialClient</c> 是具体类"——所以只能靠实跑碰运气。
/// <para>现在开的是一个 <c>internal</c> 的传输层接缝（见 <see cref="OfficialClient"/> 的构造注释）：
/// 生产路径仍然走公开构造、仍然自己建 <c>HttpClientHandler</c>，运行时行为一行没变；
/// 测试则能把"官方回 302 到登录页"这种没法按需复现的响应喂进来。</para>
/// <para>对应验收项：<b>TC-D5</b>（注入风控响应 → 整批中止、直达不受影响）与
/// <b>TC-E6</b>（官方侧失效 → 清本地会话并提示，不静默重登）。</para>
/// </summary>
public class RiskAndSessionTests
{
    /// <summary>延时不真等，只把时钟往前推——这样"间隔被强制"和"批次没跑完"都能被断言。</summary>
    private sealed class FakeClock : IGateClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
        public DateTimeOffset UtcNow => Now;

        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Now += delay;
            return Task.CompletedTask;
        }
    }

    /// <summary>按 URL 决定回什么的假传输层。记录每个被请求到的地址，用于断言"没再发第三次"。</summary>
    private sealed class ScriptedHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            return Task.FromResult(respond(url));
        }
    }

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location, UriKind.Absolute) } };

    private static RequestGate NewGate() => new(
        new FakeClock(),
        new RequestGateOptions(TimeSpan.FromSeconds(3), 3, 10, 12, 200, 100));

    /// <summary>闸门要求每次调用都写清是哪个接口（SPEC-007 九：API 标签只进日志与文档）。</summary>
    private const string Api = "API-99 测试";

    /// <summary>写进临时会话文件的一个假 Cookie。只用于让存储进入"已登录"态，不含任何真实凭据。</summary>
    private static (ProtectedSessionStore store, string path) SeededSession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rq-test-session-{Guid.NewGuid():N}.bin");
        var store = new ProtectedSessionStore(path);
        store.AdoptFromLoginPage("JSESSIONID=abcdefghijklmnop; tk=0123456789");
        return (store, path);
    }

    [Fact]
    public async Task 官方回登录页时本地会话立刻清掉并且原样上报()
    {
        // TC-E6 的服务层那一半：留着一份官方已经不认的会话反复撞，既浪费额度又把风险压在账号上。
        var (store, path) = SeededSession();
        Assert.True(store.HasValidSession);

        var handler = new ScriptedHandler(url =>
            url.Contains("lcquery")
                ? Redirect("https://kyfw.12306.cn/otn/passport?redirect=/otn/leftTicket/init")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var client = new OfficialClient(store, handler);
        var service = new TransferQueryService(client, NewGate(), store, Samples.Stations.Value);

        var ex = await Assert.ThrowsAsync<QueryException>(
            () => service.SearchAsync(Beijing(), Shanghai(), new DateOnly(2026, 10, 20)));

        Assert.Equal(QueryErrorKind.SessionRequired, ex.Kind);
        Assert.False(store.HasValidSession);
        Assert.False(File.Exists(path));
        File.Delete(path);
    }

    [Fact]
    public async Task 一次风控信号让整批扩展中止而不是跳过继续()
    {
        // TC-D5：官方一旦开始拒绝，继续挨个试就是撞风控。已拿到的结果照常返回，
        // 但后面的候选一个都不许再查——这条判据的意义全在"没再发"上。
        var gate = NewGate();
        var runner = new RejectingRunner(gate, MainViewModelTests.Journey("G1"));
        var service = new ExtensionQueryService(
            new FixedStopService(Route()), runner, gate, Samples.Stations.Value);

        var batch = await service.RunAsync(
            MainViewModelTests.Journey("G1"), new DateOnly(2026, 10, 20), maxExtraStations: 3);

        Assert.Equal(ExtensionStopReason.AbortedByUpstream, batch.StopReason);
        Assert.Equal(2, runner.Calls);                       // 第二条就是被拒的那条
        Assert.Equal(runner.Calls, batch.RequestsIssued);    // 闸门数到的次数 = 真发出去的次数
        Assert.Single(batch.Outcomes);
        // 三个候选都进了本轮预算，中止后实际没查的是剩下两个——这个数以前算的是"超出预算的部分"，
        // 于是整批中止时界面会写"未查候选 0 个"，把用户最该知道的一件事说反了。
        Assert.Equal(2, batch.CandidatesNotQueried);
    }

    [Fact]
    public async Task 用户点停止时同样是整批收尾并且保留已获结果()
    {
        // 与上一条同一个出口形状（结果保留、不再发请求），但原因不同——界面要能分清"我停的"和"官方拒的"。
        var gate = NewGate();
        var runner = new CancelAfterFirstRunner(gate, MainViewModelTests.Journey("G1"));
        var service = new ExtensionQueryService(
            new FixedStopService(Route()), runner, gate, Samples.Stations.Value);

        var batch = await service.RunAsync(
            MainViewModelTests.Journey("G1"), new DateOnly(2026, 10, 20), maxExtraStations: 3);

        Assert.Equal(ExtensionStopReason.UserStopped, batch.StopReason);
        Assert.Single(batch.Outcomes);
        Assert.Equal(1, batch.RequestsIssued);
        Assert.Equal(2, batch.CandidatesNotQueried);
    }

    [Fact]
    public void 测试接缝不对外暴露()
    {
        // 用户定的口径是"可以为了测试改生产代码，但要么还原、要么不暴露"。
        // 这里选的是"留着但不暴露"，所以这条断言就是那句话的可执行形态：
        // 公开构造只剩一个（生产路径永远拿不到假 handler），带 handler 的那个必须是 internal。
        var publicCtors = typeof(OfficialClient).GetConstructors();      // 只返回 public
        Assert.Single(publicCtors);
        Assert.DoesNotContain(publicCtors, c => c.GetParameters().Length == 2);

        var seam = typeof(OfficialClient).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.Contains(seam, c => c.GetParameters() is { Length: 2 } p &&
                                   p[1].ParameterType == typeof(System.Net.Http.HttpMessageHandler));
    }

    // ── 假件 ─────────────────────────────────────────────────

    private static Station Beijing() => Samples.Stations.Value.FindByName("北京南")!;
    private static Station Shanghai() => Samples.Stations.Value.FindByName("上海虹桥")!;

    /// <summary>一条真实存在的站序（站名都来自内置码表）。两端用 <see cref="MainViewModelTests"/> 那个
    /// 假车次真的区间（北京南 / 上海虹桥），前后各留可扩的站。</summary>
    private static TrainRoute Route()
    {
        string[] names = ["沧州西", "北京南", "德州东", "济南西", "南京南", "上海虹桥", "杭州东"];
        var stops = names.Select((n, i) =>
        {
            var st = Samples.Stations.Value.FindByName(n)
                     ?? throw new InvalidOperationException($"内置码表里没有 {n}，这条测试的站序需要换");
            return new StopDetail($"{i + 1:00}", n, i == 0 ? null : new TimeSpan(8, 0, 0) + TimeSpan.FromHours(i),
                i == names.Length - 1 ? null : new TimeSpan(8, 5, 0) + TimeSpan.FromHours(i),
                i == 0 || i == names.Length - 1 ? null : "5分钟", i == 0, i == names.Length - 1);
        }).ToList();
        return new TrainRoute("240000G10L", "G1", stops);
    }

    private sealed class FixedStopService(TrainRoute route) : ITrainStopService
    {
        public Task<TrainRoute> GetRouteAsync(
            string trainNo, string fromTelecode, string toTelecode,
            DateOnly departDate, CancellationToken ct = default) => Task.FromResult(route);
    }

    /// <summary>真实的 runner 会经闸门发请求，所以假件也必须经闸门——
    /// 否则 <c>ExtensionBatch.RequestsIssued</c> 数到的是 0，那条"实发次数"的判据就成了空话。</summary>
    private sealed class RejectingRunner(RequestGate gate, TrainJourney first) : IDirectQueryRunner
    {
        public int Calls { get; private set; }

        public async Task<IReadOnlyList<TrainJourney>> RunAsync(
            DirectQueryRequest request, ActionKind kind, CancellationToken ct = default)
        {
            await gate.RunAsync(kind, Api, _ =>
            {
                Calls++;
                if (Calls > 1) throw new QueryException(QueryErrorKind.UpstreamRejected, "官方返回 status=false");
                return Task.FromResult(1);
            }, ct);
            return [first];
        }
    }

    private sealed class CancelAfterFirstRunner(RequestGate gate, TrainJourney first) : IDirectQueryRunner
    {
        public int Calls { get; private set; }

        public async Task<IReadOnlyList<TrainJourney>> RunAsync(
            DirectQueryRequest request, ActionKind kind, CancellationToken ct = default)
        {
            if (Calls >= 1) throw new OperationCanceledException();
            await gate.RunAsync(kind, Api, _ => { Calls++; return Task.FromResult(1); }, ct);
            return [first];
        }
    }
}
