using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RouteQuery.App.Converters;

/// <summary>"枚举值等于给定参数 → 可见"。用于按视图状态切换整块区域。</summary>
public sealed class ViewStateToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var want = parameter as string;
        return value?.ToString() == want ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("单向转换器");
}

/// <summary>bool → Visibility；参数为 "invert" 时取反。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Equals(parameter, "invert")) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("单向转换器");
}

/// <summary>集合数量 &gt; 0 → true。候选弹层的开合绑这个，而不是绑一个额外的是否可见标志。</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("单向转换器");
}

/// <summary>
/// "当前档位 == 这一项" → 勾选状态（FR-35 主题单选）。
/// 绑定写成 <c>Mode=OneWay</c>，勾选只负责显示，写回与换色都由代码后置那一条路径做——
/// 同一个状态有两条提交路径就会互相覆盖（DEC-17 踩过的坑，这里不再开第二条）。
/// </summary>
public sealed class OptionEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("单向转换器");
}
