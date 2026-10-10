namespace RouteQuery.Core.Ports;

/// <summary>
/// 界面文案提供器。对应 SPEC-004 五.5（文案集中管理）与五.2（ViewModel 不得引用 <c>System.Windows</c>）。
/// <para>之所以做成端口而不是让 ViewModel 直接 <c>Application.Current.FindResource</c>：
/// 那样 ViewModel 就依赖了 WPF 类型，单元测试要跑就得起来一个 UI 线程。
/// 实现方从 XAML 资源字典取值，接口本身与界面无关。</para>
/// </summary>
public interface ITextProvider
{
    /// <summary>取一条文案；键不存在时返回带键名的占位串，让漏配在界面上立刻可见而不是显示空白。</summary>
    string Get(string key);
}
