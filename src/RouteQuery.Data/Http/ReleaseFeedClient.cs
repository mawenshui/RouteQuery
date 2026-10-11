using System.Net.Http;
using System.Text.Json;
using RouteQuery.Core.Ports;

namespace RouteQuery.Data.Http;

/// <summary>
/// GitHub Releases 版本检查（FR-36 / SPEC-007 六.1 的例外条目）。
/// <para>四条不能省，每一条都对应需求里一个可抓包验证的判据：</para>
/// <list type="number">
/// <item><b>独立 HttpClient</b>，不与 <see cref="OfficialClient"/> 共享 CookieContainer，也不经过
/// <see cref="RequestGate"/>（DEC-15）。复用官方客户端会把 12306 的会话 Cookie 带进 GitHub 请求，
/// 那是凭据外流最短的一条路径；进闸门则会让"取一个版本号"占掉亲友当天的官方请求额度。</item>
/// <item><b>5 秒超时</b>，且任何失败一律返回 null——不抛、不弹、不写错误级日志（FR-36 失败处理）。</item>
/// <item><b>请求不携带任何本机信息</b>：只有固定的产品名 User-Agent（GitHub API 强制要求 UA），
/// 没有机器名、用户名、查询记录、收藏、站点、日期。</item>
/// <item><b>不由本类决定发起时机</b>：调用方只在启动时调一次；这里没有定时器也没有重试。</item>
/// </list>
/// </summary>
public sealed class ReleaseFeedClient : IReleaseFeed
{
    /// <summary>仓库标识。<b>项目里唯一允许出现的非官方地址</b>，与官方地址同样收敛在数据层。</summary>
    private const string Repository = "mawenshui/RouteQuery";

    /// <summary>发布页。给界面"点开看"用，由常量拼出而不是取响应里的 html_url。</summary>
    public string ReleasePageUrl => $"https://github.com/{Repository}/releases/latest";

    private static readonly Uri LatestApi = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>
    /// 静态共享一个实例：整个应用生命周期只发一次请求，但即使如此也不该每次都新建连接。
    /// <c>UseCookies = false</c> 是显式的隔离声明——这个客户端永远不携带任何 Cookie。
    /// </summary>
    private static readonly HttpClient Http =
        new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<string?> FetchLatestVersionAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            request.Headers.UserAgent.ParseAdd("RouteQuery");

            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return Normalize(doc.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null);
        }
        catch (Exception)
        {
            // 断网、超时、被墙、返回不是 JSON、任务被取消——全部一样处理：什么都不说。
            // 这里连日志都不写（FR-36 隔离条），否则"检查更新失败"会在日志里长得像故障。
            return null;
        }
    }

    /// <summary>官方 tag 习惯带 v 前缀；去不掉就说明这个 tag 不是版本号，按取不到处理。</summary>
    private static string? Normalize(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? null : tag.TrimStart('v', 'V').Trim();
}
