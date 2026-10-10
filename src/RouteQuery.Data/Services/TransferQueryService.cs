using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Data.Services;

/// <summary>
/// 中转查询编排。一次用户动作 = 至多两次官方请求（会话初始化 + 取数），走同一个闸门。
/// <para><b>失效即清除</b>：官方在这个接口上用 302 → passport 表达"会话不认"。
/// 此时必须先把本地会话删掉再报错（设计 §6.2）——留着一个已知无效的会话反复撞，
/// 既浪费额度，又把风险压在亲友的账号上。</para>
/// </summary>
public sealed class TransferQueryService(
    OfficialClient client, RequestGate gate, ProtectedSessionStore session, IStationRepository stations)
    : ITransferQueryService
{
    public async Task<TransferQueryResult> SearchAsync(
        Station from, Station to, DateOnly travelDate, CancellationToken cancellationToken = default)
    {
        if (!session.HasValidSession)
            throw new QueryException(QueryErrorKind.SessionRequired, "本机没有登录态");

        string body;
        using (gate.BeginAction(ActionKind.Basic))
        {
            try
            {
                if (!client.HasSession)
                {
                    await gate.RunAsync(ActionKind.Basic, OfficialClient.ApiSession,
                        ct => client.EnsureSessionAsync(ct), cancellationToken).ConfigureAwait(false);
                }

                var resp = await gate.RunAsync(ActionKind.Basic, OfficialClient.ApiTransfer,
                    ct => client.GetTransferAsync(from.Telecode, to.Telecode, travelDate, ct),
                    cancellationToken).ConfigureAwait(false);
                body = resp.Body;
            }
            catch (QueryException ex) when (ex.Kind == QueryErrorKind.SessionRequired)
            {
                session.Clear();
                throw;   // 清完本地会话原样上抛：界面据此回到引导态，不显示成"出错了"
            }
        }

        var parsed = TransferParser.Parse(body, stations);
        return new TransferQueryResult(parsed.Plans, parsed.DroppedForNegativeWait, parsed.DroppedForShape);
    }
}
