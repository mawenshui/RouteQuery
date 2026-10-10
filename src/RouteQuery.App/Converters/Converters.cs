using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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

/// <summary>有票/无票两种行的前景色。注意界面始终同时带文字（"有"/"候补"），
/// 颜色只是加强，不作为唯一信息载体（NFR-06）。</summary>
public sealed class HasTicketToBrushConverter : IValueConverter
{
    private static readonly Brush HasTicket = Frozen(Color.FromRgb(0xB3, 0x54, 0x1E));
    private static readonly Brush SoldOut = Frozen(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HasTicket : SoldOut;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("单向转换器");
}
