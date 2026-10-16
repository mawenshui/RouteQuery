using System.Globalization;
using System.Text.Json;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 中转接口响应 → <see cref="TransferPlan"/> 列表。对应 FR-06。
/// <para>三条不容妥协的规则：</para>
/// <list type="number">
/// <item><description><b>结构自检不过就整批判 <c>ParseFailure</c></b>，不"能解几条算几条"。
/// 字段缺失往往意味着官方改了形态，此时半份结果比没有结果更危险。</description></item>
/// <item><description><b>负候车时长的方案直接丢弃</b>（并计数上报）。那说明两段时间对不上——
/// 要么官方给的就是坏数据，要么我们理解错了字段，两种情况下都不该把它端给用户。</description></item>
/// <item><description><b>票价一律不填</b>。实测这一个响应里没有任何价格字段，
/// 所以界面必须说"票价未给出"而不是显示 0 或省掉这一说（NFR-18）。</description></item>
/// </list>
/// </summary>
public static class TransferParser
{
    /// <summary>一次解析的产出，含被丢弃的方案数——界面与日志都要用它说清"为什么少了"。</summary>
    public sealed record Result(IReadOnlyList<TransferPlan> Plans, int DroppedForNegativeWait, int DroppedForShape);

    public static Result Parse(string json, IStationRepository stations)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            throw QueryException.ParseFailure("中转响应不是合法 JSON");
        }

        if (!JsonShape.IsObject(root))
            throw QueryException.ParseFailure("中转响应的最外层不是对象");
        if (!JsonShape.Prop(root, "data", out var data))
            throw QueryException.ParseFailure("中转响应没有 data 这一项");

        // 实测（2026-10-14 日志，三次）：登录态不被认的那一刻，官方回的是合法 JSON，
        // 但 data 是一段文字而不是对象。这一条判 DataUnavailable 而不是 ParseFailure——
        // 后者等于叫用户去要一个新版本，而该做的是重新登录或等一会儿（AGENTS 七.6 三者不得合并）。
        if (!JsonShape.IsObject(data))
            throw QueryException.DataUnavailable("中转响应的 data 不是数据对象，官方这一项没给");

        if (!JsonShape.Prop(data, "middleList", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw QueryException.ParseFailure("中转响应缺少 data.middleList");
        }

        var plans = new List<TransferPlan>();
        var negativeWait = 0;
        var badShape = 0;

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !HasAll(item, TransferFieldMap.RequiredPlanFields))
            {
                badShape++;
                continue;
            }

            var waitMinutes = ReadInt(item, "wait_time_minutes");
            if (waitMinutes < 0)
            {
                negativeWait++;
                continue;
            }

            var legs = ReadLegs(item, stations);
            if (legs is null)
            {
                badShape++;
                continue;
            }

            var travelDate = ReadDate(item, "train_date");
            var arrivalDate = ReadDate(item, "arrive_date");
            // 官方自己给的两个日期互相矛盾时，这条方案的第几天到达无法确定；
            // 宁可少一条并说明，也不能拿"当天到达"去显示一趟其实是次日的车。
            if (arrivalDate < travelDate)
            {
                badShape++;
                continue;
            }

            var totalMinutes = ReadInt(item, "all_lishi_minutes");
            plans.Add(new TransferPlan(
                legs[0], legs[1],
                Resolve(stations, Str(item, "middle_station_code"), Str(item, "middle_station_name")),
                TimeSpan.FromMinutes(waitMinutes),
                waitMinutes,
                // 官方这个字段是"4小时18分钟"这种中文，不是 hh:mm：时长取分钟数，原文只用于显示。
                TimeSpan.FromMinutes(totalMinutes),
                totalMinutes,
                Str(item, "all_lishi"),
                travelDate,
                arrivalDate,
                Str(item, "same_train") == "Y",
                PricesKnown: false));
        }

        if (plans.Count == 0 && list.GetArrayLength() > 0)
            throw QueryException.ParseFailure($"中转响应有 {list.GetArrayLength()} 条方案，但没有一条能按当前字段表解出来");

        return new Result(plans, negativeWait, badShape);
    }

    private static TransferLeg[]? ReadLegs(JsonElement plan, IStationRepository stations)
    {
        var full = plan.GetProperty("fullList");
        if (full.ValueKind != JsonValueKind.Array || full.GetArrayLength() != 2) return null;

        var legs = new TransferLeg[2];
        var i = 0;
        foreach (var leg in full.EnumerateArray())
        {
            if (leg.ValueKind != JsonValueKind.Object || !HasAll(leg, TransferFieldMap.RequiredLegFields)) return null;

            legs[i++] = new TransferLeg(
                Str(leg, "station_train_code"),
                Str(leg, "train_no"),
                Resolve(stations, Str(leg, "from_station_telecode"), Str(leg, "from_station_name")),
                Resolve(stations, Str(leg, "to_station_telecode"), Str(leg, "to_station_name")),
                ReadClock(leg, "start_time"),
                ReadClock(leg, "arrive_time"),
                ReadDuration(leg, "lishi"),
                ReadInt(leg, "day_difference"),
                Seats(leg),
                ReadLegDate(leg));
        }
        return legs;
    }

    /// <summary>段内日期用的是 <c>yyyyMMdd</c>，与方案层的 <c>yyyy-MM-dd</c> 不是同一种写法，
    /// 所以这里必须显式按格式解析：宽松解析会把 <c>20261011</c> 读成 <c>2026-10-11</c> 之外的某一天，
    /// 而 FR-15 拿它当 <c>API-04</c> 的 <c>depart_date</c>，错一天就取回另一天的时刻表。</summary>
    private static DateOnly ReadLegDate(JsonElement leg)
    {
        var raw = Str(leg, "start_train_date");
        return DateOnly.TryParseExact(raw, TransferFieldMap.LegDateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw Drift("start_train_date", raw, "yyyyMMdd 的段内日期");
    }

    /// <summary>
    /// 席别余票。这里<b>不</b>给票价（官方这个响应里没有），也<b>不</b>判候补
    /// （没有车次级候补标记，宁可说"无"也不造一个"可候补"）。
    /// </summary>
    private static IReadOnlyList<SeatAvailability> Seats(JsonElement leg)
    {
        var result = new List<SeatAvailability>();
        foreach (var (seatClass, field) in TransferFieldMap.SeatFields)
        {
            if (!leg.TryGetProperty(field, out _)) continue;
            var raw = Str(leg, field);
            result.Add(new SeatAvailability(
                seatClass, raw,
                AvailabilityDecoder.Decode(raw, PurchaseState.Unknown, seatClass, trainSupportsWaitlist: false),
                Price: null));
        }
        return result;
    }

    private static Station Resolve(IStationRepository stations, string telecode, string name) =>
        stations.FindByTelecode(telecode)
        ?? new Station(name, telecode, string.Empty, string.Empty, string.Empty, string.Empty);

    private static bool HasAll(JsonElement el, string[] fields) =>
        fields.All(f => el.TryGetProperty(f, out _));

    private static string Str(JsonElement el, string field) =>
        el.TryGetProperty(field, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString() : string.Empty;

    /// <summary>
    /// 时刻字段（<c>start_time</c> / <c>arrive_time</c>）必须是 <c>HH:mm</c>。
    /// 解不出来一律判 <c>ParseFailure</c>：退化成 <c>00:00</c> 会把一趟下午的车显示成凌晨，
    /// 而那是会直接把人送上错车的错（AGENTS 六.2「禁止猜着往下解析」）。
    /// </summary>
    private static TimeOnly ReadClock(JsonElement el, string field)
    {
        var raw = Str(el, field);
        return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out var t)
            ? t : throw Drift(field, raw, "HH:mm 的时刻");
    }

    /// <summary>单段历时是 <c>hh\:mm</c>（如 "03:14"），超过一天的用 day_difference 表达。
    /// <b>总历时不是这个格式</b>，见 <c>TotalDurationText</c>。</summary>
    private static TimeSpan ReadDuration(JsonElement el, string field)
    {
        var raw = Str(el, field);
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var t)
            ? t : throw Drift(field, raw, "hh:mm 的时长");
    }

    private static DateOnly ReadDate(JsonElement el, string field)
    {
        var raw = Str(el, field);
        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var d)
            ? d : throw Drift(field, raw, "yyyy-MM-dd 的日期");
    }

    /// <summary>数值字段解不出来不许当 0：候车 0 分钟、全程 0 分钟都是凭空造出来的数（NFR-18）。</summary>
    private static int ReadInt(JsonElement el, string field)
    {
        var raw = Str(el, field);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : throw Drift(field, raw, "整数");
    }

    private static QueryException Drift(string field, string raw, string expected) =>
        QueryException.ParseFailure($"中转响应字段 {field} 的值“{raw}”不是预期的{expected}，停止出数");
}
