namespace RouteQuery.Core.Ports;

/// <summary>
/// 剩余额度的只读视图。界面用它显示"今日还可查询 N 次"（FR-27），
/// 但不暴露闸门本身——避免 ViewModel 能顺手调用发请求的能力。
/// </summary>
public interface IQueryBudget
{
    int DailyRemaining { get; }
}

/// <summary>应用设置。刻意只放"成本与风控的刹车"类项目，不做偏好开关的杂物抽屉（FR-22）。
/// 唯一的偏好项是 <c>Theme</c>：FR-35 把它列为 V1 需求，而它不产生任何请求、不接触数据，
/// 与刹车类项目不同在于它只影响呈现。</summary>
/// <param name="MaxExtensionStations">"多买几站"的最大站数，1–10。它决定候选规模；实际请求次数另有硬上限。</param>
/// <param name="DisclaimerAccepted">首次启动的非官方声明是否已确认（FR-20）。只记"确认过"这一件事，
/// 不记时间戳——留时间戳就成了偏好项，超出本类型"刹车"的定位。</param>
/// <summary>界面主题档位（FR-35）。放在 Core 是因为视图模型不许引用 WPF，
/// 而"选哪一档"是设置值不是渲染状态。</summary>
public enum ThemeChoice
{
    /// <summary>跟随系统（FR-35 的默认档）：读 Windows 的浅色/深色设置，只在启动与手动切换时读一次。</summary>
    FollowSystem,

    Light,
    Dark,
}

/// <param name="CheckUpdatesEnabled">启动时是否检查一次新版本（FR-36）。默认开，但 SPEC-007 六.1
/// 把"必须可关闭"写成这个例外的成立条件之一——所以它是刹车而不是偏好：关掉后非官方出站必须是 0。</param>
/// <param name="Theme">主题档位（FR-35），默认跟随系统。</param>
public sealed record AppSettings(int MaxExtensionStations = 3, bool DisclaimerAccepted = false,
    bool CheckUpdatesEnabled = true, ThemeChoice Theme = ThemeChoice.FollowSystem)
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
