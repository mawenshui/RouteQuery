namespace RouteQuery.Data.Stations;

/// <summary>
/// 官方站点码表的字段布局。<b>与 <c>LeftTicketColumnLayout</c> 同等待遇</b>：
/// 码表同样是位置式格式（以 <c>|</c> 分隔），一旦官方调整字段顺序，
/// 错位的结果是"站名变成三字码"这种看起来正常的坏数据，所以字段下标也必须单点收敛。
/// <para>实测样本：<c>@bjn|北京南|VNP|beijingnan|bjn|3|0357|北京|||</c>（2026-10-10）。</para>
/// </summary>
internal static class StationCodeTableLayout
{
    /// <summary>单条记录所需的最少字段数；不足即丢弃该条，不做补位猜测。</summary>
    internal const int MinFields = 8;

    /// <summary>0：检索键（小写站名的缩写，如 bjn）。界面不使用它，匹配走下面三个拼音字段。</summary>
    internal const int Key = 0;

    /// <summary>1：站名全称。</summary>
    internal const int Name = 1;

    /// <summary>2：三字码，与官方接口交互的唯一凭据。</summary>
    internal const int Telecode = 2;

    /// <summary>3：全拼。</summary>
    internal const int Pinyin = 3;

    /// <summary>4：简拼。</summary>
    internal const int ShortPinyin = 4;

    /// <summary>5：序号，界面不用。</summary>
    internal const int Ordinal = 5;

    /// <summary>6：城市代码。同城聚合依赖它——这是实测发现的，省掉了维护第二张同城映射表。</summary>
    internal const int CityCode = 6;

    /// <summary>7：城市名。</summary>
    internal const int CityName = 7;
}
