using System.Globalization;
using System.Text.Json;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// <c>API-02</c> 直达余票响应的解析器。对应 FR-05。
/// <para>处理顺序固定为"顶层形态 → 结构自检 → 逐列取值 → 席别与票价"，
/// 其中结构自检不过一律判 <see cref="QueryErrorKind.ParseFailure"/> 并<b>整批不出数</b>
/// （设计 5.1；NFR-16）。</para>
/// </summary>
public sealed class LeftTicketParser(IStationRepository stations)
{
    /// <summary>本次解析遇到的未知席别短码，供上层写诊断日志。</summary>
    public List<string> LastUnknownSeatCodes { get; private set; } = [];

    /// <summary>
    /// 解析响应正文。
    /// </summary>
    /// <exception cref="QueryException">
    /// HTML 或非 JSON → <see cref="QueryErrorKind.UpstreamRejected"/>；
    /// 结构自检不过 → <see cref="QueryErrorKind.ParseFailure"/>。
    /// 空 result <b>不</b>算异常，返回空列表由界面渲染 NoResult（FR-10）。
    /// </exception>
    public IReadOnlyList<TrainJourney> Parse(string body)
    {
        if (StructureValidator.LooksLikeHtml(body))
            throw new QueryException(QueryErrorKind.UpstreamRejected, "响应是 HTML 页面而非车次数据");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new QueryException(QueryErrorKind.UpstreamRejected, ex.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;

            // 官方的业务成功位与 HTTP 位分开。status=false 时 data 可能为空壳，不能当无结果。
            if (root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.False)
                throw new QueryException(QueryErrorKind.UpstreamRejected, "官方返回 status=false");

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Array)
            {
                throw QueryException.ParseFailure("响应缺少 data.result 数组");
            }

            var namesFromResponse = ReadStationNameMap(data);
            var records = new List<string[]>();
            foreach (var item in result.EnumerateArray())
            {
                var text = item.GetString();
                if (text is null) continue;
                records.Add(text.Split('|'));
            }

            StructureValidator.AssertValid(records);

            var journeys = new List<TrainJourney>(records.Count);
            LastUnknownSeatCodes = [];
            foreach (var cols in records)
            {
                var j = ToJourney(cols, namesFromResponse);
                if (j is not null) journeys.Add(j);
            }
            return journeys;
        }
    }

    private TrainJourney? ToJourney(string[] c, IReadOnlyDictionary<string, string> fallbackNames)
    {
        var trainCode = c[LeftTicketColumnLayout.TrainCode.Index];
        var trainNo = c[LeftTicketColumnLayout.TrainNo.Index];
        var fromCode = c[LeftTicketColumnLayout.FromStationTelecode.Index];
        var toCode = c[LeftTicketColumnLayout.ToStationTelecode.Index];
        var startCode = c[LeftTicketColumnLayout.StartStationTelecode.Index];
        var endCode = c[LeftTicketColumnLayout.EndStationTelecode.Index];

        // 三字码必须能在码表里落地。落不了地说明码表过期或响应异常，此时宁可不显示这趟车，
        // 也不拿 data.map 之外的猜测凑站名（NFR-18）。
        var from = Resolve(fromCode, fallbackNames) ?? throw QueryException.DataUnavailable($"出发站三字码 {fromCode} 未收录");
        var to = Resolve(toCode, fallbackNames) ?? throw QueryException.DataUnavailable($"到达站三字码 {toCode} 未收录");
        var start = Resolve(startCode, fallbackNames) ?? from;
        var end = Resolve(endCode, fallbackNames) ?? to;

        var departure = ParseClock(c[LeftTicketColumnLayout.Departure.Index]);
        var arrival = ParseClock(c[LeftTicketColumnLayout.Arrival.Index]);
        var duration = ParseClock(c[LeftTicketColumnLayout.Duration.Index]);

        // 次日判定依据是官方给出的历时，而不是"到达时刻比发车小"这种界面侧推断：
        // 出发 + 历时 越过 24 小时即为次日到达（BF-05）。
        var arrivesNextDay = departure + duration >= TimeSpan.FromDays(1);

        var purchase = MapPurchase(c[LeftTicketColumnLayout.CanWebBuy.Index]);
        var waitlistAllowed = c[LeftTicketColumnLayout.HoubuTrainFlag.Index] == "1";
        var fares = PriceDecoder.Decode(c[LeftTicketColumnLayout.FareString.Index], out var unknownCodes);
        LastUnknownSeatCodes.AddRange(unknownCodes);

        var seats = new List<SeatAvailability>();
        foreach (var (seatClass, spec) in LeftTicketColumnLayout.AvailabilityColumns)
        {
            var raw = c[spec.Index];
            if (string.IsNullOrEmpty(raw)) continue; // 该车次没有这个席别，不显示空行

            seats.Add(new SeatAvailability(
                seatClass,
                raw,
                AvailabilityDecoder.Decode(raw, purchase, seatClass, waitlistAllowed),
                fares.TryGetValue(seatClass, out var price) ? price : null));
        }

        return new TrainJourney(
            trainCode,
            trainNo,
            from,
            to,
            start,
            end,
            departure,
            arrival,
            duration,
            arrivesNextDay,
            purchase,
            seats,
            ParseSaleTime(c[LeftTicketColumnLayout.SaleTime.Index]));
    }

    private Station? Resolve(string telecode, IReadOnlyDictionary<string, string> fallbackNames)
    {
        var station = stations.FindByTelecode(telecode);
        if (station is not null) return station;

        // 码表缺站时用响应自带的名字兜底建一条临时记录：站名是官方给的，不是猜的，
        // 但三字码之外的拼音信息缺失，因此不参与搜索。
        return fallbackNames.TryGetValue(telecode, out var name)
            ? new Station(name, telecode, string.Empty, string.Empty, string.Empty, name)
            : null;
    }

    private static IReadOnlyDictionary<string, string> ReadStationNameMap(JsonElement data)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in m.EnumerateObject())
                map[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }
        return map;
    }

    private static PurchaseState MapPurchase(string raw) => raw switch
    {
        "Y" => PurchaseState.Buyable,
        "N" => PurchaseState.NotBuyable,
        "IS_TIME_NOT_BUY" => PurchaseState.NotYetOnSale,
        _ => PurchaseState.Unknown,
    };

    private static TimeSpan ParseClock(string value) =>
        TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var ts)
            ? ts
            : throw QueryException.ParseFailure($"时刻无法解析：\"{value}\"");

    /// <summary>放票时间格式为 yyyyMMddHHmm；官方对部分车次返回空串，此时返回 null 而不是猜测。</summary>
    private static DateTimeOffset? ParseSaleTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParseExact(
                value, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
            ? dto
            : null;
    }
}
