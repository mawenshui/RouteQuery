using System.Net;
using System.Text;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.Data.Http;

/// <summary>一次上游调用的结果。刻意不自动跟随重定向——官方用 302 告诉我们真实路径。</summary>
/// <param name="Body">响应体；被重定向时为空。</param>
/// <param name="RedirectTo">官方引导到的绝对地址；没有重定向时为 null。</param>
public sealed record UpstreamResponse(string Body, string? RedirectTo);

/// <summary>
/// 官方接口的传输层。<b>一次方法调用 = 一次 HTTP 请求</b>，这个约束是为了让
/// <see cref="RequestGate"/> 的计数与 SPEC-007 的"请求数"字面对得上——
/// 如果把"发现后缀 + 重定向重试"藏在一次方法调用里，预算上限就只是名义上成立。
/// <para>本类型与 <see cref="EndpointResolver"/> 是全项目仅有的两处允许出现官方地址的地方。</para>
/// <para>不做的事更多：不重试、不换请求头组合、不解密任何字段（SPEC-007 三）。</para>
/// </summary>
public sealed class OfficialClient : IDisposable
{
    /// <summary>官方站点根地址。也是"允许读哪一站 Cookie"的唯一出处。</summary>
    public const string Origin = "https://kyfw.12306.cn";
    public const string InitUrl = Origin + "/otn/leftTicket/init";

    /// <summary>日志里用的接口标签。SPEC-007 第九节规定 <c>API-xx</c> 只出现在文档与日志，
    /// 不进界面——所以这些常量属于客户端层，业务代码只引用它们。</summary>
    public const string ApiSession = "API-01 会话";
    public const string ApiLeftTicket = "API-02 余票";
    public const string ApiStopStations = "API-04 经停站";
    public const string ApiStationNames = "API-01 站点码表";
    private const string LeftTicketPath = Origin + "/otn/leftTicket/";
    private const string StopStationsPath = Origin + "/otn/czxx/queryByTrainNo";
    private const string StationNamesPath = Origin + "/otn/resources/js/framework/station_name.js";

    /// <summary>只用一个常见桌面 UA，不做指纹轮换（SPEC-007 三.6）。</summary>
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private readonly HttpClient _http;
    private readonly ProtectedSessionStore? _session;
    private bool _sessionReady;

    /// <param name="session">登录态存储。为 null 时应用就是纯游客态（本版默认形态之一）。
    /// 注意这里传的是<b>存储本身</b>而不是 Cookie 字符串——Cookie 只在发请求的一刻被读出来，
    /// 不进入任何长驻字段，也不出现在方法签名上（SPEC-007 三.2）。</param>
    public OfficialClient(ProtectedSessionStore? session = null)
    {
        _session = session;
        _http = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(64),
            UseCookies = true,
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(15),   // NFR-01 硬性上限
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", InitUrl);
    }

    /// <summary>会话是否已建立。建立一次即可，之后所有查询复用同一容器。</summary>
    public bool HasSession => _sessionReady;

    /// <summary>是否已持有官方登录态。只回答有/无，不返回内容。</summary>
    public bool HasLoginSession => _session?.HasValidSession ?? false;

    /// <summary>余票接口的当前地址。后缀由 <see cref="EndpointResolver"/> 缓存。</summary>
    public string LeftTicketUrl(string pathName) => LeftTicketPath + SafeName(pathName);

    /// <summary>余票接口的查询串。日期用 <c>yyyy-MM-dd</c>（实测另一种格式会失败）。</summary>
    public static string LeftTicketQuery(DirectQueryRequest r) => new StringBuilder()
        .Append("?leftTicketDTO.train_date=").Append(r.TravelDate.ToString("yyyy-MM-dd"))
        .Append("&leftTicketDTO.from_station=").Append(Uri.EscapeDataString(r.From.Telecode))
        .Append("&leftTicketDTO.to_station=").Append(Uri.EscapeDataString(r.To.Telecode))
        .Append("&purpose_codes=ADULT")
        .ToString();

    /// <summary>建立基础会话（一次请求）。</summary>
    public async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (_sessionReady) return;
        await GetAsync(InitUrl, string.Empty, ct).ConfigureAwait(false);
        _sessionReady = true;
    }

    /// <summary>取余票数据（一次请求）。被重定向时 <see cref="UpstreamResponse.RedirectTo"/> 非空。</summary>
    public Task<UpstreamResponse> GetLeftTicketAsync(string url, string query, CancellationToken ct) =>
        GetAsync(url, query, ct);

    /// <summary>取经停站（一次请求）。响应是具名字段 JSON，不需要列布局表。</summary>
    public Task<UpstreamResponse> GetStopsAsync(
        string trainNo, string fromTelecode, string toTelecode, DateOnly travelDate, CancellationToken ct) =>
        GetAsync(StopStationsPath,
            "?train_no=" + Uri.EscapeDataString(trainNo)
            + "&from_station_telecode=" + Uri.EscapeDataString(fromTelecode)
            + "&to_station_telecode=" + Uri.EscapeDataString(toTelecode)
            + "&depart_date=" + travelDate.ToString("yyyy-MM-dd"),   // 必须是这个格式
            ct);

    /// <summary>取官方站点码表原文（一次请求）。用于设置页的"更新站点数据"（FR-18）。</summary>
    public Task<UpstreamResponse> GetStationNamesAsync(CancellationToken ct) =>
        GetAsync(StationNamesPath, string.Empty, ct);

    /// <summary>直接取一个绝对地址，用于跟随官方引导的新路径。</summary>
    public Task<UpstreamResponse> GetAbsoluteAsync(string url, CancellationToken ct) =>
        GetAsync(url, string.Empty, ct);

    /// <summary>从 302 的 Location 造出可再次请求的绝对地址；含登录页时抛 <see cref="QueryErrorKind.SessionRequired"/>。</summary>
    public static string? ResolveRedirect(string fromUrl, string? location)
    {
        if (location is null) return null;
        var absolute = Uri.IsWellFormedUriString(location, UriKind.Absolute)
            ? location
            : new Uri(new Uri(fromUrl), location).ToString();

        // 302 到 passport 是"需要你登录"，302 到别的路径是"接口改名了"，处置完全不同。
        if (absolute.Contains("passport", StringComparison.OrdinalIgnoreCase) ||
            absolute.Contains("login", StringComparison.OrdinalIgnoreCase))
            throw new QueryException(QueryErrorKind.SessionRequired, "官方要求登录后才能取该数据");

        return absolute;
    }

    private static string SafeName(string name) =>
        name.Length is > 0 and < 40 && !name.Contains('/') && !name.Contains('?') && !name.Contains(' ')
            ? name
            : "query";

    private async Task<UpstreamResponse> GetAsync(string url, string query, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            // 登录 Cookie 逐次附带，而不是塞进长驻的 CookieContainer：
            // 用户在设置页点"退出并清除"之后，下一个请求就必须是干净的，
            // 不能靠"容器里的旧值等官方把它判死"。
            using var request = new HttpRequestMessage(HttpMethod.Get, url + query);
            var cookie = _session?.ReadCookieHeaderForRequest();
            if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);

            resp = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new QueryException(QueryErrorKind.Timeout);
        }
        catch (TaskCanceledException)
        {
            throw;   // 用户主动取消：原样上抛，界面按取消处理，不显示成错误
        }
        catch (HttpRequestException ex)
        {
            throw new QueryException(QueryErrorKind.NetworkUnavailable, ex.Message);
        }

        using (resp)
        {
            if (resp.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.Redirect &&
                resp.Headers.Location is { } location)
            {
                return new UpstreamResponse(string.Empty, ResolveRedirect(url, location.ToString()));
            }

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new QueryException(QueryErrorKind.UpstreamRejected, $"HTTP {(int)resp.StatusCode}");

            return new UpstreamResponse(body, null);
        }
    }

    public void Dispose() => _http.Dispose();
}
