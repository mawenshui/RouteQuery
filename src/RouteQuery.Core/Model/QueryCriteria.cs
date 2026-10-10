namespace RouteQuery.Core.Model;

/// <summary>列车类型分组。车次号首字母即类别，纯数字为普客（实测存在 1461 这类车次）。</summary>
public enum TrainKind
{
    GaoTie,     // G
    DongChe,    // D
    ChengJi,    // C
    ZhiDa,      // Z 直达特快
    TeKuai,     // T 特快
    KuaiSu,     // K 快速
    PuKe,       // 纯数字 普客
    LinKe,      // L 临客
    Other,
}

/// <summary>出发时段，四段等分一天（FR-11）。</summary>
public enum DepartureBand { Night, Morning, Afternoon, Evening }

/// <summary>
/// 本地筛选与排序条件。对应 FR-11 / FR-12。
/// <para>全部在内存生效，<b>不重新请求官方接口</b>——断网时已加载的结果仍然可以筛和排。</para>
/// </summary>
public sealed record QueryCriteria
{
    /// <summary>车型白名单；为空表示不限。</summary>
    public IReadOnlyCollection<TrainKind> Kinds { get; init; } = [];

    /// <summary>出发时段白名单；为空表示不限。</summary>
    public IReadOnlyCollection<DepartureBand> Bands { get; init; } = [];

    /// <summary>要求该席别有票；为空表示不限。</summary>
    public SeatClass? RequiredSeat { get; init; }

    /// <summary>只看有余票的车次。<b>候补与未开售都不算有票</b>（FR-11 口径）。</summary>
    public bool OnlyAvailable { get; init; }

    /// <summary>只看当天到达（排除次日到达的夜车）。</summary>
    public bool SameDayArrival { get; init; }

    /// <summary>是否处于任何生效的筛选，用于决定"清除筛选"按钮是否可用。</summary>
    public bool HasAnyFilter =>
        Kinds.Count > 0 || Bands.Count > 0 || RequiredSeat is not null || OnlyAvailable || SameDayArrival;

    /// <summary>由车次号推断列车类型。纯数字是普客，而不是"未知"。</summary>
    public static TrainKind KindOf(string trainCode) => trainCode switch
    {
        null or "" => TrainKind.Other,
        _ => trainCode[0] switch
        {
            'G' => TrainKind.GaoTie,
            'D' => TrainKind.DongChe,
            'C' => TrainKind.ChengJi,
            'Z' => TrainKind.ZhiDa,
            'T' => TrainKind.TeKuai,
            'K' => TrainKind.KuaiSu,
            'L' => TrainKind.LinKe,
            >= '0' and <= '9' => TrainKind.PuKe,
            _ => TrainKind.Other,
        }
    };

    /// <summary>界面显示的中文车型名。</summary>
    public static string KindLabel(TrainKind kind) => kind switch
    {
        TrainKind.GaoTie => "高铁",
        TrainKind.DongChe => "动车",
        TrainKind.ChengJi => "城际",
        TrainKind.ZhiDa => "直达特快",
        TrainKind.TeKuai => "特快",
        TrainKind.KuaiSu => "快速",
        TrainKind.PuKe => "普客",
        TrainKind.LinKe => "临客",
        _ => "其他",
    };

    /// <summary>按出发时刻归入时段。</summary>
    public static DepartureBand BandOf(TimeSpan departure) => departure.Hours switch
    {
        >= 0 and < 6 => DepartureBand.Night,
        >= 6 and < 12 => DepartureBand.Morning,
        >= 12 and < 18 => DepartureBand.Afternoon,
        _ => DepartureBand.Evening,
    };
}
