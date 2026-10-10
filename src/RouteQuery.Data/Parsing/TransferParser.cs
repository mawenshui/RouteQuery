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

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("middleList", out var list)
            || list.ValueKind != JsonValueKind.Array)
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

            var waitMinutes = Int(item, "wait_time_minutes");
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

            plans.Add(new TransferPlan(
                legs[0], legs[1],
                Resolve(stations, Str(item, "middle_station_code"), Str(item, "middle_station_name")),
                TimeSpan.FromMinutes(waitMinutes),
                waitMinutes,
                // 官方这个字段是"4小时18分钟"这种中文，不是 hh:mm：时长取分钟数，原文只用于显示。
                TimeSpan.FromMinutes(Int(item, "all_lishi_minutes")),
                Int(item, "all_lishi_minutes"),
                Str(item, "all_lishi"),
                ParseDate(item, "train_date"),
                ParseDate(item, "arrive_date"),
                Str(item, "same_station") == "1",
                Str(item, "isOutStation") != "0",
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
                TimeOnly.Parse(Str(leg, "start_time")),
                TimeOnly.Parse(Str(leg, "arrive_time")),
                ParseClock(leg, "lishi"),
                int.TryParse(Str(leg, "day_difference"), out var d) ? d : 0,
                Seats(leg));
        }
        return legs;
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
            if (!leg.TryGetProperty(field, out var el)) continue;
            var raw = el.GetString() ?? string.Empty;
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

    private static int Int(JsonElement el, string field) =>
        int.TryParse(Str(el, field), out var v) ? v : 0;

    /// <summary>单段历时是 <c>hh\:mm</c>（如 "03:14"），超过一天的用 day_difference 表达。
    /// <b>总历时不是这个格式</b>，见 <c>TotalDurationText</c>。</summary>
    private static TimeSpan ParseClock(JsonElement el, string field) =>
        TimeSpan.TryParse(Str(el, field), System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero;

    private static DateOnly ParseDate(JsonElement el, string field) =>
        DateOnly.TryParse(Str(el, field), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : DateOnly.MinValue;
}
