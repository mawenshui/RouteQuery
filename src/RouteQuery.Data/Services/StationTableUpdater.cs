using RouteQuery.Core.Errors;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;
using RouteQuery.Data.Stations;

namespace RouteQuery.Data.Services;

/// <summary>
/// 站点码表的手动更新。对应 FR-18 的"更新安全"四条：下载到临时文件 → 校验条目数量下限与格式 →
/// 原子替换 → <b>失败则保留旧数据并提示</b>。
/// <para>校验不过时这里返回失败而不是抛异常：码表更新失败是可恢复的，界面要给出的是一句
/// "这次没更新成，还在用原来那份"，不是一个崩溃。</para>
/// </summary>
public sealed class StationTableUpdater(OfficialClient client, RequestGate gate) : IStationTableUpdater
{
    public async Task<StationTableUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        string body;
        using (gate.BeginAction(ActionKind.Drilldown))
        {
            try
            {
                var resp = await gate.RunAsync(ActionKind.Drilldown, OfficialClient.ApiStationNames,
                    ct => client.GetStationNamesAsync(ct), cancellationToken).ConfigureAwait(false);
                body = resp.Body;
            }
            catch (QueryException ex)
            {
                return StationTableUpdateResult.Failed(ex.Kind switch
                {
                    QueryErrorKind.NetworkUnavailable => "连不上网络，这次没更新成。原来的站点数据还能正常用。",
                    QueryErrorKind.Timeout => "官方响应有点慢，这次没更新成。原来的站点数据还能正常用。",
                    _ => "官方这次没给站点数据。原来的站点数据还能正常用。",
                });
            }
        }

        // 日期用"今天"而不是从文件里猜：官方这份 JS 不带版本日期，实测口径就是"当天取到的即当天的"。
        var parsed = StationNameLoader.ParseFromOfficial(body, DateOnly.FromDateTime(DateTime.Today));
        if (parsed is null)
            return StationTableUpdateResult.Failed("官方给的站点表读不出完整内容，可能只下载了一半。已保留原来的站点数据。");

        StationTableLoader.SaveUpdated(parsed);
        return new StationTableUpdateResult(true, parsed.Items.Count, parsed.SourceDate, null);
    }
}
