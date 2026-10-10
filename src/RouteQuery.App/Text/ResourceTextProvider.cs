using System.Windows;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.Text;

/// <summary>从 XAML 资源字典取文案的实现（<c>Resources/文案.xaml</c>）。</summary>
public sealed class ResourceTextProvider : ITextProvider
{
    public string Get(string key)
    {
        var value = Application.Current.TryFindResource(key);
        return value as string ?? $"[缺少文案 {key}]";   // 漏配时让它在界面上刺眼，而不是显示空白
    }
}
