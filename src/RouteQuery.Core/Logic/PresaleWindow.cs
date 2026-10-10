namespace RouteQuery.Core.Logic;

/// <summary>
/// 查询日期的可选范围。对应 FR-03。
/// <para><b>实测结论（2026-10-10）：预售期为"含当天共 15 天"</b>——当天可查 10-24，
/// 而 10-25 起官方返回 HTML 错误页。</para>
/// <para>刻意做成可配置的参数而不是常量：官方这个口径近几年调整过多次，写死就会在变更后
/// 让用户点进一个必然失败的日期。更要紧的是，<b>越界响应与风控拒绝的形态完全相同</b>
/// （SPEC-007 v1.7 规则 8），所以一旦本地不拦住，界面会把"日期越界"显示成"官方暂时不可用"，
/// 用户会反复重试而问题永不自愈。</para>
/// </summary>
public static class PresaleWindow
{
    /// <summary>预售期天数上限（含当天）。可由配置覆盖。</summary>
    public const int DefaultPresaleDays = 15;

    /// <summary>返回 [今天, 今天 + presaleDays - 1] 的可选闭区间。</summary>
    public static (DateOnly Min, DateOnly Max) For(DateOnly today, int presaleDays = DefaultPresaleDays)
    {
        var days = Math.Clamp(presaleDays, 1, 60);
        return (today, today.AddDays(days - 1));
    }

    /// <summary>该日期是否可查。UI 置灰之外还要过这一关——双重拒绝，不依赖控件行为（FR-03 验收②）。</summary>
    public static bool IsSelectable(DateOnly date, DateOnly today, int presaleDays = DefaultPresaleDays)
    {
        var (min, max) = For(today, presaleDays);
        return date >= min && date <= max;
    }
}
