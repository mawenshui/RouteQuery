using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// 历史列表里的一行。历史存的是三字码（稳定标识），显示要还成站名，
/// 所以这一层唯一的职责就是<b>查码表把码换成人看的字</b>。
/// </summary>
public sealed class HistoryRowViewModel(QueryHistoryEntry entry, IStationRepository stations, ITextProvider text)
{
    public string FromTelecode { get; } = entry.FromTelecode;
    public string ToTelecode { get; } = entry.ToTelecode;
    public DateOnly TravelDate { get; } = entry.TravelDate;

    public string RouteText { get; } =
        $"{NameOf(entry.FromTelecode, stations, text)}→{NameOf(entry.ToTelecode, stations, text)}";

    public string WhenText { get; } = entry.At.ToString("M月d日 HH:mm");

    public string CountText { get; } = string.Format(text.Get("历史_结果数"), entry.ResultCount);

    // 静态而不是实例方法：属性初始化里调不到实例成员，而这里正需要在初始化时把码换成字。
    private static string NameOf(string telecode, IStationRepository stations, ITextProvider text) =>
        stations.FindByTelecode(telecode)?.Name ?? text.Get("历史_未知站");
}
