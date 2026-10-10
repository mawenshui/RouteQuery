namespace RouteQuery.Core.Model;

/// <summary>
/// 一个车站。城市代码与城市名来自官方站点码表的第 7、8 字段，用于同城聚合（FR-01）。
/// </summary>
/// <param name="Name">站名全称，如"北京南"。界面上永远只显示全称，避免同城歧义。</param>
/// <param name="Telecode">三字码（如 VNP），是与官方接口交互的唯一凭据，不得出现在界面。</param>
/// <param name="Pinyin">全拼，如 beijingnan。</param>
/// <param name="ShortPinyin">简拼，如 bjn，用于首字母匹配。</param>
/// <param name="CityCode">城市代码，如 0357。同城不同车站共享它。</param>
/// <param name="CityName">城市名，如"北京"。</param>
public sealed record Station(
    string Name,
    string Telecode,
    string Pinyin,
    string ShortPinyin,
    string CityCode,
    string CityName)
{
    /// <summary>是否为该城市的同城站（用于候选排序与展示分组）。</summary>
    public bool SameCityAs(Station other) => CityCode.Length > 0 && CityCode == other.CityCode;
}
