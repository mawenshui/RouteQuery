namespace RouteQuery.Core.Ports;

/// <summary>一次码表更新的结果。失败原因必须是能直接给人看的一句话。</summary>
public sealed record StationTableUpdateResult(bool Succeeded, int Count, DateOnly SourceDate, string? FailureReason)
{
    public static StationTableUpdateResult Failed(string reason) => new(false, 0, DateOnly.MinValue, reason);
}

/// <summary>
/// 站点码表的手动更新。对应 FR-18。
/// <para>只有手动，没有自动：启动时<b>不</b>请求更新。这条限制的意义是"应用不会一打开就向官方发请求"，
/// 它同时也是频率红线的一部分。</para>
/// </summary>
public interface IStationTableUpdater
{
    Task<StationTableUpdateResult> UpdateAsync(CancellationToken cancellationToken = default);
}
