using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// 经停时刻表里的一行。对应 FR-14 与界面说明"站序 · 站名 · 到时 · 发时 · 停留，
/// 当前查询 OD 两站高亮并加 ▲/▼ 标记"。
/// <para>存在的理由只有两个：① 时刻要预格式化成文本，绑 <c>TimeSpan?</c> 再在 XAML 里写
/// <c>StringFormat</c> 极易静默失效；② ▲/▼ 标记需要知道本次查询的 OD，
/// 那是界面层的事实，不该塞进数据层的 <see cref="StopDetail"/>。</para>
/// </summary>
public sealed class StopRowViewModel(StopDetail stop, string fromName, string toName, ITextProvider text)
{
    public string StationNo { get; } = stop.StationNo;
    public string StationName { get; } = stop.StationName;

    /// <summary>到时。始发站官方给的是占位符，这里写"始发"——留空会让人以为漏了一列。</summary>
    public string ArriveText { get; } = stop.ArriveTime is { } t
        ? t.ToString(@"hh\:mm")
        : stop.IsFirst ? text.Get("经停_始发") : text.Get("提示_官方未给出");

    public string DepartText { get; } = stop.DepartureTime is { } t
        ? t.ToString(@"hh\:mm")
        : stop.IsLast ? text.Get("经停_终到") : text.Get("提示_官方未给出");

    /// <summary>停留时长。首末站本就没有"停留"这一说，写"—"；中间站缺失才写"官方未给出"（NFR-18）。</summary>
    public string StopoverText { get; } = stop.Stopover is { Length: > 0 } s
        ? s
        : stop.IsFirst || stop.IsLast ? text.Get("经停_不适用") : text.Get("提示_官方未给出");

    /// <summary>▲ 上车站 / ▼ 下车站。站名与查询条件同源（同一份码表），按全称精确比对。</summary>
    public string Marker { get; } = Match(stop.StationName, toName) ? "▼"
        : Match(stop.StationName, fromName) ? "▲"
        : string.Empty;

    public bool IsHighlighted => Marker.Length > 0;

    private static bool Match(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}
