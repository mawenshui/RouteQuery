using System.Windows;
using Microsoft.Win32;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.Theme;

/// <summary>
/// 主题切换（FR-35）。机制只有一句话：<b>把 MergedDictionaries 里那份色板字典换掉</b>，
/// 样式字典（主题.xaml）与所有界面文件都不动——它们引用的是键名，不是色值。
/// <para>为什么能"即时生效"：样式里的颜色全部写成 <c>DynamicResource</c>。
/// 用 <c>StaticResource</c> 的话切换主题后界面不会重解析，深色档位就变成"只在新启动时生效"，
/// 而需求要求设置页切换即时生效。</para>
/// </summary>
public static class ThemeManager
{
    private const string LightSource = "Resources/浅色.xaml";
    private const string DarkSource = "Resources/深色.xaml";

    /// <summary>应用当前档位后的实际主题（跟随系统会在这里落地成浅或深）。</summary>
    public static ThemeChoice Effective { get; private set; } = ThemeChoice.Light;

    public static void Apply(ThemeChoice choice)
    {
        var target = choice switch
        {
            ThemeChoice.Dark => ThemeChoice.Dark,
            ThemeChoice.Light => ThemeChoice.Light,
            // "跟随系统"读的是 Windows 自己的浅色/深色设置。只在启动与手动切换时读一次：
            // 监听系统设置变化需要常驻钩子，而 FR-35 没有要求运行时跟随（应用内也不许有定时器）。
            _ => SystemPrefersDark() ? ThemeChoice.Dark : ThemeChoice.Light,
        };

        // MergedDictionaries 是有序集合：找到那一份色板换掉，找不到就追加到末尾（末尾优先级最高）
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var index = IndexOfPalette(dictionaries);
        var palette = new ResourceDictionary { Source = new Uri(Pack(target), UriKind.Absolute) };

        if (index < 0) dictionaries.Add(palette);
        else dictionaries[index] = palette;

        Effective = target;
    }

    private static string Pack(ThemeChoice target) =>
        $"pack://application:,,,/RouteQuery;component/{(target == ThemeChoice.Dark ? DarkSource : LightSource)}";

    private static int IndexOfPalette(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source is not null && (source.EndsWith(LightSource, StringComparison.Ordinal)
                                    || source.EndsWith(DarkSource, StringComparison.Ordinal)))
                return i;
        }
        return -1;
    }

    /// <summary>系统是否偏好深色。读不到（旧系统、键被删、无权限）一律按浅色处理，不猜也不报。</summary>
    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
