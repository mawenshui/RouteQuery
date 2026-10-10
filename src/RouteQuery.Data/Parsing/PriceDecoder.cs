using System.Globalization;
using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 票价串（<c>yp_info_new</c>，列 39）解码。
/// <para>编码规则：<b>每 10 字符一条记录 = 席别短码(1) + 以分为单位的票价(6) + 附加标记(3)</b>。
/// 该规则已跨高铁（G/D/C）与普速（K/T/Z）车型验证，且解码值与官方 <c>queryTicketPrice</c> 接口
/// 返回逐项一致（SPEC-007 第二节"票价来源结论"）。</para>
/// </summary>
public static class PriceDecoder
{
    private const int RecordLength = 10;
    private const int CodeLength = 1;
    private const int CentsLength = 6;

    /// <summary>
    /// 解出一个车次在各席别上的票面价。<b>同一席别出现多条记录时取第一条</b>。
    /// <para>这不是保守选择而是被官方接口反向验证过的策略：官方的 <c>queryTicketPrice</c>
    /// 只返回与首条一致的值，次条（实测如 O=49.03、1=21.53）官方并不展示，其业务含义未知。
    /// 因此把次条标成"儿童票/学生票"就是猜，违反 NFR-18（FR-34）。</para>
    /// </summary>
    /// <param name="fareString">列 39 原始串。空串返回空字典。</param>
    /// <param name="unknownCodes">输出参数：遇到未登记的席别短码时收集起来，供诊断日志使用。</param>
    public static Dictionary<SeatClass, decimal> Decode(string fareString, out List<string> unknownCodes)
    {
        unknownCodes = [];
        var result = new Dictionary<SeatClass, decimal>();
        if (string.IsNullOrEmpty(fareString)) return result;

        if (fareString.Length % RecordLength != 0)
        {
            // 长度不合规意味着官方改了编码结构。此时不猜，交由上层按 DataUnavailable 处理该席别维度。
            return result;
        }

        for (var offset = 0; offset + RecordLength <= fareString.Length; offset += RecordLength)
        {
            var record = fareString.AsSpan(offset, RecordLength);
            var code = record[..CodeLength].ToString();
            var centsPart = record.Slice(CodeLength, CentsLength);

            if (!int.TryParse(centsPart, NumberStyles.None, CultureInfo.InvariantCulture, out var cents))
                continue;

            if (SeatCodeMap.IsUnknown(code))
                unknownCodes.Add(code);

            var seatClass = SeatCodeMap.SeatClassOf(code);
            if (seatClass is null) continue;

            // 首条优先：已存在即跳过后续同席别记录。
            result.TryAdd(seatClass.Value, cents / 100m);
        }

        return result;
    }

    /// <summary>
    /// 校验一个票价串是否形如官方结构（长度为 10 的整数倍且数字部分可解析）。
    /// 用于把"官方换了编码"与"这个车次没有价格"区分开。
    /// </summary>
    public static bool LooksWellFormed(string fareString)
    {
        if (string.IsNullOrEmpty(fareString)) return true;
        if (fareString.Length % RecordLength != 0) return false;

        for (var offset = 0; offset < fareString.Length; offset += RecordLength)
        {
            foreach (var ch in fareString.AsSpan(offset + CodeLength, CentsLength + 3))
            {
                if (!char.IsAsciiDigit(ch)) return false;
            }
        }
        return true;
    }
}
