using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Parsing;

namespace RouteQuery.Data.Http;

/// <summary>
/// 车次经停站。对应 FR-14（P0），同时是区间扩展的站序来源。
/// <para>响应是具名字段数组，不需要列布局表；因此这里唯一的解析风险是<b>字段缺失</b>。</para>
/// </summary>
public sealed class TrainStopService(OfficialClient client, RequestGate gate) : ITrainStopService
{
    /// <summary>会话内缓存：展开过的车次不再重复请求（FR-14 验收③、SPEC-007 四）。</summary>
    private readonly Dictionary<string, TrainRoute> _cache = [];

    public async Task<TrainRoute> GetRouteAsync(
        string trainNo, string fromTelecode, string toTelecode, DateOnly travelDate,
        CancellationToken cancellationToken = default)
    {
        var key = $"{trainNo}|{fromTelecode}|{toTelecode}|{travelDate:yyyy-MM-dd}";
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        string body;
        using (var window = gate.BeginAction(ActionKind.Drilldown))
        {
            if (!client.HasSession)
            {
                await gate.RunAsync(ActionKind.Drilldown,
                    ct => client.EnsureSessionAsync(ct), cancellationToken).ConfigureAwait(false);
            }

            var resp = await gate.RunAsync(ActionKind.Drilldown,
                ct => client.GetStopsAsync(trainNo, fromTelecode, toTelecode, travelDate, ct),
                cancellationToken).ConfigureAwait(false);
            body = resp.Body;
        }

        var route = StopStationParser.Parse(trainNo, body);
        lock (_cache) _cache[key] = route;
        return route;
    }
}
