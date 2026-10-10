using RouteQuery.Core.Errors;
using RouteQuery.Core.Logic;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;

namespace RouteQuery.Data.Services;

/// <summary>
/// 区间扩展（"多买几站"）的批次执行。对应 FR-26 ~ FR-28。
/// <para>三条实现纪律，都是这个功能被允许存在的前提：</para>
/// <list type="number">
/// <item><description><b>严格串行、间隔不小于 3 秒</b>。耗时长达几十秒是有意结果，不是缺陷；
/// 因此必须增量推送进度，否则用户只会以为程序卡死。</description></item>
/// <item><description><b>预算硬顶在 12 次</b>，与用户设的站数上限无关。站数上限是"候选规模"，
/// 预算是"实际打了多少次"，两道刹车各管一件事。</description></item>
/// <item><description>遇到<b>风控或结构异常时整批中止</b>，而不是跳过这个候选继续下一个——
/// 在风控面前持续撞是把节流彻底架空。</description></item>
/// </list>
/// </summary>
public sealed class ExtensionQueryService(
    ITrainStopService stopService,
    IDirectQueryRunner runner,
    RequestGate gate,
    IStationRepository stations) : IExtensionQueryService
{
    public async Task<ExtensionBatch> RunAsync(
        TrainJourney target,
        DateOnly travelDate,
        int maxExtraStations,
        IProgress<ExtensionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 站序拿不到就没有候选。这是报错，不是"退回猜邻近站"（NFR-18）。
        var route = await stopService.GetRouteAsync(
            target.TrainNo, target.From.Telecode, target.To.Telecode, travelDate, cancellationToken)
            .ConfigureAwait(false);

        var stops = route.Stops;
        var fromIndex = ExtensionCandidateGenerator.IndexOf(stops, target.From.Name);
        var toIndex = ExtensionCandidateGenerator.IndexOf(stops, target.To.Name);

        if (fromIndex < 0 || toIndex < 0 || fromIndex >= toIndex)
            throw QueryException.DataUnavailable("官方给出的经停站里找不到本次查询的两个站，无法计算扩展方案");

        var candidates = ExtensionCandidateGenerator.Generate(stops, fromIndex, toIndex, maxExtraStations);
        var budget = Math.Clamp(maxExtraStations, 1, 10);
        var taken = candidates.Take(budget).ToList();

        var outcomes = new List<ExtensionOutcome>(taken.Count);
        var stopReason = ExtensionStopReason.Completed;
        var issued = 0;

        using var window = gate.BeginAction(ActionKind.Extension, budget);

        foreach (var candidate in taken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                stopReason = ExtensionStopReason.UserStopped;
                break;
            }

            try
            {
                // 经停站接口只给站名。三字码必须从码表解析出来——拿站名当三字码去请求官方
                // 等于伪造标识符，且会静默查到错的东西（NFR-18）。
                if (!TryResolve(target.From, candidate.BoardStation, out var board) ||
                    !TryResolve(target.To, candidate.AlightStation, out var alight))
                {
                    var skipped = new ExtensionOutcome(candidate, null, NotSoldHere: true);
                    outcomes.Add(skipped);
                    progress?.Report(new ExtensionProgress(outcomes.Count, taken.Count, skipped));
                    continue;
                }

                var request = new DirectQueryRequest(board, alight, travelDate);

                var journeys = await runner
                    .RunAsync(request, ActionKind.Extension, cancellationToken)
                    .ConfigureAwait(false);
                issued++;

                // 只认同一趟车。匹配不到就标"此区间不售"，绝不拿相邻车次顶替——
                // 那会把用户导向另一趟他本来没看上的车。
                var same = journeys.FirstOrDefault(j => j.TrainNo == target.TrainNo);
                var outcome = new ExtensionOutcome(candidate, same, NotSoldHere: same is null);
                outcomes.Add(outcome);
                progress?.Report(new ExtensionProgress(outcomes.Count, taken.Count, outcome));
            }
            catch (OperationCanceledException)
            {
                stopReason = ExtensionStopReason.UserStopped;
                break;
            }
            catch (RateLimitedException)
            {
                // 预算或当日额度到了：正常收尾，已拿到的结果照常返回。
                stopReason = ExtensionStopReason.Completed;
                break;
            }
            catch (QueryException ex) when (ex.Kind is QueryErrorKind.UpstreamRejected or QueryErrorKind.ParseFailure)
            {
                stopReason = ExtensionStopReason.AbortedByUpstream;
                break;
            }
            catch (QueryException ex) when (ex.Kind is QueryErrorKind.NetworkUnavailable or QueryErrorKind.Timeout)
            {
                // 单个候选的网络抖动不牵连同批：记为不可售并继续，让用户看到"哪些没查到"。
                outcomes.Add(new ExtensionOutcome(candidate, null, NotSoldHere: true));
                issued++;
                progress?.Report(new ExtensionProgress(outcomes.Count, taken.Count, outcomes[^1]));
            }
        }

        return new ExtensionBatch(outcomes, candidates.Count - taken.Count, stopReason, gate.CurrentActionIssued);
    }

    /// <summary>
    /// 把经停站还原成带三字码的车站。若正好是本次区间的端点则复用已知对象，
    /// 否则按站名查码表；查不到就返回 false，由调用方跳过该候选。
    /// </summary>
    private bool TryResolve(Station endpoint, StopDetail stop, out Station station)
    {
        if (string.Equals(endpoint.Name, stop.StationName, StringComparison.Ordinal))
        {
            station = endpoint;
            return true;
        }

        var found = stations.FindByName(stop.StationName);
        station = found ?? endpoint;
        return found is not null;
    }
}
