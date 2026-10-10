using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Data.Http;

/// <summary>
/// 在指定动作窗口内执行一次直达查询。让"扩展批次"能复用同一条查询链路，
/// 而不必自己再实现一遍解析与重定向处理。
/// </summary>
public interface IDirectQueryRunner
{
    Task<IReadOnlyList<TrainJourney>> RunAsync(
        DirectQueryRequest request, ActionKind kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// 直达余票查询。对应 FR-05。
/// <para><b>一次 HTTP 请求对应一次闸门记账</b>：会话初始化、取数据、跟随官方引导的重定向各自计数，
/// 这样 SPEC-007 的"单动作 ≤ 3 次"才是可核对的事实而不是名义上的。</para>
/// </summary>
public sealed class TrainQueryService(
    OfficialClient client, RequestGate gate, LeftTicketParser parser, EndpointResolver endpoints)
    : ITrainQueryService, IDirectQueryRunner
{
    /// <summary>界面直接调用：自开一个基础动作窗口。</summary>
    public async Task<IReadOnlyList<TrainJourney>> SearchAsync(
        DirectQueryRequest request, CancellationToken cancellationToken = default)
    {
        using var window = gate.BeginAction(ActionKind.Basic);
        return await RunAsync(request, ActionKind.Basic, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrainJourney>> RunAsync(
        DirectQueryRequest request, ActionKind kind, CancellationToken cancellationToken = default)
    {
        if (!client.HasSession)
        {
            await gate.RunAsync(kind, OfficialClient.ApiSession,
                ct => client.EnsureSessionAsync(ct), cancellationToken).ConfigureAwait(false);
        }

        var url = client.LeftTicketUrl(endpoints.LeftTicketName);
        var query = OfficialClient.LeftTicketQuery(request);

        var first = await gate.RunAsync(kind, OfficialClient.ApiLeftTicket,
            ct => client.GetLeftTicketAsync(url, query, ct), cancellationToken).ConfigureAwait(false);

        var body = first.Body;

        if (first.RedirectTo is { } redirect)
        {
            // 路径后缀漂移。跟随一次，并把新后缀记进缓存，下次就不用再花一次重定向的额度。
            var discovered = EndpointResolver.SuffixFrom(redirect);
            if (discovered is not null) endpoints.SaveLeftTicketSuffix(discovered);

            var second = await gate.RunAsync(kind, OfficialClient.ApiLeftTicket + "(跟随重定向)",
                ct => client.GetAbsoluteAsync(redirect, ct), cancellationToken).ConfigureAwait(false);
            body = second.Body;
        }

        var journeys = parser.Parse(body);

        return journeys;
    }
}

