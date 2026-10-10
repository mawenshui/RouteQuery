namespace RouteQuery.Core.Ports;

/// <summary>
/// 剩余额度的只读视图。界面用它显示"今日还可查询 N 次"（FR-27），
/// 但不暴露闸门本身——避免 ViewModel 能顺手调用发请求的能力。
/// </summary>
public interface IQueryBudget
{
    int DailyRemaining { get; }
}

/// <summary>应用设置。刻意只放"成本与风控的刹车"类项目，不做偏好开关的杂物抽屉（FR-22）。</summary>
/// <param name="MaxExtensionStations">"多买几站"的最大站数，1–10。它决定候选规模；实际请求次数另有硬上限。</param>
/// <param name="DisclaimerAccepted">首次启动的非官方声明是否已确认（FR-20）。只记"确认过"这一件事，
/// 不记时间戳——留时间戳就成了偏好项，超出本类型"刹车"的定位。</param>
public sealed record AppSettings(int MaxExtensionStations = 3, bool DisclaimerAccepted = false)
{
    public const int MinStations = 1;
    public const int MaxStations = 10;

    /// <summary>读取与保存都过一遍钳制：设置文件被手改成 99 也拿不到更多请求（FR-29 验收③）。</summary>
    public AppSettings Clamped() => this with
    {
        MaxExtensionStations = Math.Clamp(MaxExtensionStations, MinStations, MaxStations),
    };
}

/// <summary>设置的读写。对应 FR-22。</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
