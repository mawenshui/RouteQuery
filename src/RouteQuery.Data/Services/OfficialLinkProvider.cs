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

    /// <summary>只读官方站自己的 Cookie 范围，别的站点一概不读。</summary>
    public string CookieScopeUrl => OfficialClient.Origin;
}
