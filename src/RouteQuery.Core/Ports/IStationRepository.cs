using RouteQuery.Core.Model;

namespace RouteQuery.Core.Ports;

/// <summary>
/// 站点码表的读取与模糊匹配。对应 FR-01 与 FR-18。
/// <para>实现方必须在应用启动时就能离线工作：内置码表可用则不发起任何网络请求。</para>
/// </summary>
public interface IStationRepository
{
    /// <summary>内置或已加载码表的数据日期，用于在状态栏显示"站点数据更新日期"（FR-18）。</summary>
    DateOnly SourceDate { get; }

    /// <summary>码表是否可用。不可用时查询按钮必须禁用并说明原因（BF-03）。</summary>
    bool IsLoaded { get; }

    /// <summary>车站总数，用于校验码表是否被截断（FR-18 更新安全）。</summary>
    int Count { get; }

    /// <summary>
    /// 按输入串给出候选站。实现方负责打分与同城聚合；<b>不得</b>把同城多站合并成一个候选（FR-01）。
    /// </summary>
    /// <param name="keyword">用户输入的站名、拼音或简拼片段。</param>
    /// <param name="take">最多返回条数。</param>
    IReadOnlyList<Station> Search(string keyword, int take = 12);

    /// <summary>按三字码取站；未收录时返回 null（调用方据此报 InputInvalid，不发请求）。</summary>
    Station? FindByTelecode(string telecode);

    /// <summary>
    /// 按站名精确取站。用于把经停站接口返回的<b>站名</b>还原成三字码——
    /// 那个接口不返回三字码，而拿站名冒充三字码去请求官方是伪造标识符，绝不允许。
    /// 未收录时返回 null，调用方应放弃该候选并说明原因，而不是猜一个。
    /// </summary>
    Station? FindByName(string name);
}
