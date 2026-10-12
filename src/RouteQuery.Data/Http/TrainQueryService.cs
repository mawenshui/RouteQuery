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
            var discovered = EndpointResolver.SuffixFrom(redirect);
            if (discovered is null)
                // 官方有时把请求丢到非查询路径（2026-10-12 实测见过 302 到 www.12306.cn 的 error.html）。
                // 那是"这次请求不被接受"，不是"数据格式变了"。跟进去只会拿回一段 HTML，
                // 然后把一个风控信号报成"等新版"——用户会傻等一个根本不会来的更新。
                throw new QueryException(QueryErrorKind.UpstreamRejected,
                    $"官方把请求引导到非查询路径：{Host(redirect)}");

            // 路径后缀漂移。跟随一次，并把新后缀记进缓存，下次就不用再花一次重定向的额度。
            endpoints.SaveLeftTicketSuffix(discovered);

            var second = await gate.RunAsync(kind, OfficialClient.ApiLeftTicket + "(跟随重定向)",
                ct => client.GetAbsoluteAsync(redirect, ct), cancellationToken).ConfigureAwait(false);
            body = second.Body;
        }

        try
        {
            var journeys = parser.Parse(body);
            if (parser.LastSuspendedMessages.Count > 0)
                gate.Note($"官方占位记录 {parser.LastSuspendedMessages.Count} 条：{parser.LastSuspendedMessages[0]}");
            if (parser.LastUnknownSeatCodes.Count > 0)
                // 未知短码意味着官方的席别编码表动了。它不影响本次出数（原样透传），
                // 但没有这行日志，下一次席别错位就会以"看不懂的数据"的形式浮出来。
                gate.Note($"未知席别短码：{string.Join(",", parser.LastUnknownSeatCodes.Distinct())}");
            return journeys;
        }
        catch (QueryException ex)
        {
            // 解析失败必须留痕。界面上那句"请把这条提示截图发我"太脆——同一个错在日志里
            // 要有一句能区分"哪一列不对"的记录，否则远程排障只能靠猜。
            gate.Note($"解析失败 {ex.Kind}: {ex.Message}");
            throw;
        }
    }

    private static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "无法识别的地址";
}

