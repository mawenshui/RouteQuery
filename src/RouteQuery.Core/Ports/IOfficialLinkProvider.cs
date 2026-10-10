namespace RouteQuery.Core.Ports;

/// <summary>
/// 官方页面地址的提供方。对应 FR-19。
/// <para>之所以要做成端口而不是在界面里写死：<b>AGENTS 第六节禁止 <c>RouteQuery.App</c> 与
/// <c>RouteQuery.Core</c> 出现任何 URL</b>，官方地址的唯一归属地是数据层。
/// 界面只负责"把地址交给系统浏览器"这一步。</para>
/// </summary>
public interface IOfficialLinkProvider
{
    /// <summary>余票查询页地址。应用只做"在系统默认浏览器里打开"，不代发任何请求。</summary>
    string LeftTicketPageUrl { get; }

    /// <summary>官方登录页地址，供 WebView2 宿主作为起始页（FR-24）。
    /// 与上一个成员同样：地址常量属于数据层，App 只拿到字符串。</summary>
    string LoginPageUrl { get; }

    /// <summary>允许读取 Cookie 的站点范围（SPEC-007 三.2 的"读取 WebView 容器的 Cookie 集合"）。
    /// <para><b>为什么是一组而不是一个</b>：官方登录页与查询页分属不同主机，Cookie 也分别落在
    /// 各自的域上；只读查询站会漏掉登录身份那一条。给一组，界面按并集取，取不到就是取不到——
    /// 但不能再因为"只问了一站"而漏。</para>
    /// 单独走端口而不是让界面写死字符串，是为了让"我们能读哪几站的 Cookie"仍然只有一个出处
    /// （AGENTS 第六节禁止 App 出现任何 URL）。</summary>
    IReadOnlyList<string> CookieScopeUrls { get; }
}