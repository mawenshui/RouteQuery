using System.Globalization;
using System.Text.Json;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 经停站响应解析。对应 FR-14 / FR-26 的站序来源。
/// <para>与余票响应不同，这里是<b>具名字段数组</b>，所以不存在列位漂移问题；
/// 风险在字段缺失与时刻占位符（始发站无到达时刻，官方给 <c>----</c>）。</para>
/// </summary>
public static class StopStationParser
{
    /// <exception cref="QueryException">
    /// 结构不是预期对象时判 <see cref="QueryErrorKind.ParseFailure"/>；
    /// 数组为空判 <see cref="QueryErrorKind.DataUnavailable"/>——
    /// <b>不得</b>改用码表推断经停站来"保住功能"（NFR-18）。
    /// </exception>
    public static TrainRoute Parse(string trainNo, string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith('<'))
            throw new QueryException(QueryErrorKind.UpstreamRejected, "经停站接口返回了非 JSON 内容");

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException ex)
        {
            throw QueryException.ParseFailure("经停站响应不是合法 JSON：" + ex.Message);
        }

        // 与中转、余票同一类隐患：TryGetProperty 只对对象合法，官方把 data 给成一段文字时
        // 它会抛 InvalidOperationException 并绕过 QueryException 一路冒到界面（JsonShape 有全文）。
        if (!JsonShape.IsObject(root))
            throw QueryException.ParseFailure("经停站响应的最外层不是对象");
        if (!JsonShape.Prop(root, "data", out var data))
            throw QueryException.ParseFailure("经停站响应没有 data 这一项");
        if (!JsonShape.IsObject(data))
            throw QueryException.DataUnavailable("经停站响应的 data 不是数据对象，官方这一项没给");

        if (!JsonShape.Prop(data, "data", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            throw QueryException.ParseFailure("经停站响应缺少 data.data 数组");
        }

        var stops = new List<StopDetail>();
        string? displayCode = null;
        var ordinal = 0;
        foreach (var item in arr.EnumerateArray())
        {
            if (!JsonShape.IsObject(item)) continue;   // 站条目不是对象就是官方换了形态或混进了占位，跳过而不是猜
            var name = Str(item, "station_name");
            var no = Str(item, "station_no");
            if (name is null || no is null) continue;   // 缺关键字段的站跳过，而不是编一个

            displayCode ??= Str(item, "station_train_code");
            ordinal++;
            stops.Add(new StopDetail(
                no,
                name,
                Clock(item, "arrive_time"),
                Clock(item, "start_time"),
                NullIfEmpty(Str(item, "stopover_time")),
                ordinal == 1,
                false));
        }

        if (stops.Count == 0)
            throw QueryException.DataUnavailable("官方没有返回该车次的经停站");

        // 终到标记要在收集完之后再打，否则中途出现跳号就会把错的站当成终点。
        for (var i = 0; i < stops.Count; i++)
            stops[i] = stops[i] with { IsLast = i == stops.Count - 1 };

        return new TrainRoute(trainNo, displayCode ?? trainNo, stops);
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>时刻字段可能是 <c>----</c> 或缺失，一律当 null 处理，绝不猜成 00:00。</summary>
    private static TimeSpan? Clock(JsonElement e, string prop)
    {
        var raw = Str(e, prop);
        if (raw is null) return null;
        return TimeSpan.TryParseExact(raw, @"hh\:mm", CultureInfo.InvariantCulture, out var ts) ? ts : null;
    }
}
