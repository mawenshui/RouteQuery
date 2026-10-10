using RouteQuery.Core.Model;

namespace RouteQuery.Core.Logic;

/// <summary>一个"多买几站"候选：把上车点前移 p 站、下车点后延 q 站后得到的区间。</summary>
/// <param name="BoardAhead">需要往前多买的上车站数 p。</param>
/// <param name="RideBeyond">需要往后多买的下车站数 q。</param>
/// <param name="BoardStation">候选的实际上车站（票面发站）。</param>
/// <param name="AlightStation">候选的实际下车站（票面到站）。</param>
/// <param name="ExtraStations">多买站数 = p + q，界面显示的就是这个数。</param>
public sealed record ExtensionCandidate(
    int BoardAhead,
    int RideBeyond,
    StopDetail BoardStation,
    StopDetail AlightStation)
{
    public int ExtraStations => BoardAhead + RideBeyond;
}

/// <summary>
/// 区间扩展候选生成。对应 FR-26，纯函数、不触网，因此可以完全离线测透。
/// <para>三条设计约定：① "站"指<b>该车次经停站序上的位次</b>，不是里程，所以必须与金额同时显示（FR-28）；
/// ② 候选区间必须<b>真包含</b>目标区间；③ 站序一律以官方的 <see cref="StopDetail.StationNo"/> 为准，
/// <b>不得</b>用数组下标假定无跳号。</para>
/// </summary>
public static class ExtensionCandidateGenerator
{
    /// <summary>产品级硬上限：最多 10 站（FR-29）。不信任调用方传入的值。</summary>
    public const int MaxExtraStations = 10;

    /// <summary>默认上限，偏保守——它是成本与风控的刹车，不是功能开关。</summary>
    public const int DefaultExtraStations = 3;

    /// <summary>
    /// 生成候选。返回结果已按"少买几站优先"排序；调用方再按请求预算截断（FR-27）。
    /// </summary>
    /// <param name="stops">该车次全程经停站序（按 StationNo 升序）。</param>
    /// <param name="targetFromIndex">目标出发站在 stops 中的下标。</param>
    /// <param name="targetToIndex">目标到达站在 stops 中的下标。</param>
    /// <param name="maxExtraStations">用户设置的上限，会被钳制到 <see cref="MaxExtraStations"/>。</param>
    public static IReadOnlyList<ExtensionCandidate> Generate(
        IReadOnlyList<StopDetail> stops,
        int targetFromIndex,
        int targetToIndex,
        int maxExtraStations = DefaultExtraStations)
    {
        if (stops.Count == 0) return [];
        if (targetFromIndex < 0 || targetToIndex >= stops.Count || targetFromIndex >= targetToIndex)
            return []; // 站序本身不成立，宁可不给候选也不造出上不了车的区间

        var k = Math.Clamp(maxExtraStations, 1, MaxExtraStations);
        var found = new Dictionary<string, ExtensionCandidate>();

        void Add(int p, int q)
        {
            var board = targetFromIndex - p;
            var alight = targetToIndex + q;
            if (board < 0 || alight > stops.Count - 1) return;
            if (p == 0 && q == 0) return;                 // 区间未扩大，就是目标区间本身
            if (board >= targetFromIndex && alight <= targetToIndex) return; // 必须真包含

            var key = $"{board}-{alight}";
            found.TryAdd(key, new ExtensionCandidate(p, q, stops[board], stops[alight]));
        }

        for (var p = 1; p <= k; p++) Add(p, 0);           // 只往前多买
        for (var q = 1; q <= k; q++) Add(0, q);           // 只往后多买
        for (var p = 1; p <= k; p++)                      // 两端同时扩，且 p+q <= k
            for (var q = 1; q <= k - p; q++) Add(p, q);

        return found.Values
            .OrderBy(c => c.ExtraStations)
            .ThenBy(c => Math.Min(c.BoardAhead, c.RideBeyond))
            .ThenBy(c => c.BoardAhead)   // 同代价时优先"往前多买"：多坐一站上车比晚下两站更容易接受
            .ThenBy(c => c.BoardStation.StationNo, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>在站序里定位一个三字码的下标；找不到返回 -1（调用方据此判 DataUnavailable）。</summary>
    public static int IndexOf(IReadOnlyList<StopDetail> stops, string telecodeName)
    {
        for (var i = 0; i < stops.Count; i++)
        {
            if (string.Equals(stops[i].StationName, telecodeName, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
