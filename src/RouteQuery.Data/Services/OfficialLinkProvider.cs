using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;

namespace RouteQuery.Data.Services;

/// <summary>
/// 官方页面地址。<b>地址常量继续留在数据层</b>（AGENTS 第六节），App 只通过端口拿到字符串。
/// <para>刻意<b>不</b>带 OD 与日期参数：官方查询页的可带参数形态仍未核实（开放问题 Q-07），
/// 拼一个猜出来的查询串等于把一个可能失效的 URL 交给亲友。FR-19 允许这种保守形态——
/// 打开官方查询页 + 界面提示用户自己填两站和日期。</para>
/// </summary>
public sealed class OfficialLinkProvider : IOfficialLinkProvider
{
    public string LeftTicketPageUrl => OfficialClient.InitUrl;

    /// <summary>登录页就是余票查询页：官方在未登录时会把它引导到 <c>/otn/passport</c>，
    /// 那里正是用户自己输账号密码的那一屏。刻意不另拼一个登录 URL（Q-07 同口径：不猜地址）。</summary>
    public string LoginPageUrl => OfficialClient.InitUrl;

    /// <summary>
    /// 只读官方自己的主机，别的站点一概不读。
    /// <para><b>为什么要带路径列好几条</b>：Cookie 是按"这个 URL 会带上哪些 Cookie"来匹配的，
    /// 包含路径。12306 的业务会话（<c>JSESSIONID</c> 那一类）通常挂在 <c>/otn</c> 路径下，
    /// 只问站点根路径就永远拿不到它们——实测"采到 7 条却仍被判未登录"很可能就是这个原因。
    /// 所以这里按真正要访问的几条路径各问一次，再取并集。</para>
    /// </summary>
    public IReadOnlyList<string> CookieScopeUrls { get; } =
    [
        OfficialClient.Origin + "/",                      // 站点根
        OfficialClient.Origin + "/otn/",                  // 业务路径：会话 Cookie 的常见挂载点
        OfficialClient.Origin + "/otn/leftTicket/init",   // 我们实际会打开的那一屏
        OfficialClient.Origin + "/otn/lcQuery/query",     // 中转接口本身要带的那几条
        "https://www.12306.cn/",                          // 登录身份可能落在这个主机上
    ];
}
