namespace RouteQuery.Data.Http;

/// <summary>可注入的时钟。抽出来的唯一目的是让"间隔 3 秒"这类规则能被测试而不必真的等 3 秒。</summary>
public interface IGateClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>生产实现。</summary>
public sealed class SystemGateClock : IGateClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
