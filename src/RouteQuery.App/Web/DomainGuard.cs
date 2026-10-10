namespace RouteQuery.App.Web;

/// <summary>
/// 官方域名判定（DEC-10 的白名单唯一来源）。
/// <para>写成纯函数而不是散在导航事件里，是为了它能被测试。<b>"这次导航放不放行"
/// 是本项目唯一一道防止"在应用内看到假 12306 页面"的闸门</b>——它一旦只能靠评审来保证，
/// 就等于没有。</para>
/// </summary>
public static class DomainGuard
{
    private const string OfficialHost = "12306.cn";

    /// <summary>
    /// 是否允许在应用内导航到这个地址。四条拒绝规则各有理由：
    /// 解析不了 → 拒绝；非 https → 拒绝（登录页走明文不可接受）；
    /// 带 <c>user@host</c> → 拒绝（那正是用来伪装域名的写法）；
    /// 主机名不是 <c>12306.cn</c> 或其子域 → 拒绝。
    /// </summary>
    public static bool IsAllowed(string? url)
    {
        if (!TryHost(url, out var host)) return false;
        return host == OfficialHost || host.EndsWith("." + OfficialHost, StringComparison.Ordinal);
    }

    /// <summary>
    /// 给界面显示的当前主机名。<b>必须</b>由调用方从 <c>CoreWebView2.Source</c> 取，
    /// 不允许我们自己拼一个字符串当"当前地址"展示——那等于让用户相信一个我们编的文本。
    /// </summary>
    public static string HostLabel(string? url) => TryHost(url, out var host) ? host : "地址无法识别";

    /// <summary>把 URL 规范成小写主机名。解析失败返回 false。</summary>
    private static bool TryHost(string? url, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        host = uri.Host.ToLowerInvariant();
        return host.Length > 0;
    }
}
