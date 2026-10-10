using System.Net;
using System.Text;
using System.Text.Json;
using RouteQuery.Data.Http;
using RouteQuery.Data.Store;

namespace RouteQuery.Tests;

/// <summary>
/// 一次性实测探针（跑完即删）：按真实路径取一次中转响应，只把结构与一条样本记录写进临时文件。
/// </summary>
public class LcQueryProbe
{
    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private static string OutPath => Path.Combine(Path.GetTempPath(), "lcquery-probe.txt");

    [Fact]
    public async Task 按真实路径取一次中转结构()
    {
        var session = new ProtectedSessionStore();
        Assert.True(session.HasValidSession, "没有登录态");
        var cookie = session.ReadCookieHeaderForRequest()!;
        var pairs = cookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(kv => kv.Split('=', 2)).Where(p => p.Length == 2).ToList();
        var tk = pairs.FirstOrDefault(p => p[0] == "tk")?[1];
        Assert.NotNull(tk);

        const string url =
            "https://kyfw.12306.cn/lcquery/queryG?train_date=2026-10-11&from_station_telecode=BJP" +
            "&to_station_telecode=SHH&middle_station=&result_index=0&can_query=Y&isShowWZ=N&purpose_codes=00&channel=E";

        using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UA);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://kyfw.12306.cn/otn/lcQuery/init");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);
        http.DefaultRequestHeaders.TryAddWithoutValidation("tk", tk);

        var sb = new StringBuilder();
        var resp = await http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();
        sb.AppendLine($"status={(int)resp.StatusCode} type={resp.Content.Headers.ContentType} length={body.Length}");
        sb.AppendLine($"head200={body[..Math.Min(200, body.Length)]}");

        var brace = body.IndexOf('{');
        if (brace >= 0)
        {
            using var doc = JsonDocument.Parse(body[brace..]);
            Walk(sb, doc.RootElement, "root", 0);
        }

        File.WriteAllText(OutPath, sb.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "lcquery-raw.json"), body, Encoding.UTF8);
        Assert.True(resp.IsSuccessStatusCode || (int)resp.StatusCode == 302);
    }

    private static void Walk(StringBuilder sb, JsonElement el, string path, int depth)
    {
        var pad = new string(' ', depth * 2);
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine($"{pad}{path}: object keys=[{string.Join(",", el.EnumerateObject().Select(p => p.Name))}]");
                foreach (var p in el.EnumerateObject()) Walk(sb, p.Value, p.Name, depth + 1);
                break;
            case JsonValueKind.Array:
                var n = el.GetArrayLength();
                sb.AppendLine($"{pad}{path}: array[{n}]");
                if (n > 0) Walk(sb, el[0], $"{path}[0]", depth + 1);
                break;
            default:
                var raw = el.ToString();
                sb.AppendLine($"{pad}{path}: {el.ValueKind} = {raw[..Math.Min(400, raw.Length)]}");
                break;
        }
    }
}
