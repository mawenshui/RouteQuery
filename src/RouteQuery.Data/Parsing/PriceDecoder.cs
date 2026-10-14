using System.Globalization;
using RouteQuery.Core.Model;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 票价串（<c>yp_info_new</c>，列 39）解码。
/// <para><b>每条记录 10 个字符 = 席别短码(1) + 以角为单位的票价数字(5) + 尾部标记(4)</b>。
/// 这个切分不是我们猜的，是官方页面渲染这段字符串时用的切分：
/// <c>var c9=dc.substring(0,1); var df=dc.substring(1,6)/10; var de=dc.substring(6,10);</c>
/// （<c>queryLeftTicket_end_js.js</c> 里生成 <c>aria-label</c> 的那个函数）。
/// 也就是说 <c>01740</c> 读作 174.0 元，尾巴四位是"这个席别还剩几张"，
/// 而 <b>尾巴 ≥ 3000 是官方给无座那条记录打的标记</b>——官方同一函数里的原文条件是
/// <c>if("WZ_"==di){if(de&gt;=3000){…无座票价…}}</c>，它判无座时<b>完全不看短码</b>。</para>
/// <para>为什么无座要另打标记而不是自己的短码：无座卖的是本车最低一档席别的价，
/// 于是官方把它的记录写成同一个短码（高铁 <c>O</c>、普速 <c>1</c>），只靠尾巴 ≥ 3000 区分。
/// 2026-10-14 实测 16 趟车全部如此，例如 G25 的 <c>O066100002</c>（二等座 661.0，剩 2 张）
/// 与 <c>O066103062</c>（无座 661.0，尾巴 3062）。</para>
/// <para>旧版本按"短码(1) + 分(6) + 附加(3)"切分，于是同一条无座记录被读成 661.03 元，
/// 并被当成"与二等座重复的记录"整条丢掉——界面因此永远显示"无座 官方未给出票价"。
/// 现在按官方切分，价格一律精确到角，不再出现凭空的"分"。</para>
/// </summary>
public static class PriceDecoder
{
    private const int RecordLength = 10;
    private const int CodeLength = 1;
    private const int JiaoLength = 5;

    /// <summary>官方用来标记"这条是无座"的尾部阈值，原文条件为 <c>de &gt;= 3000</c>。</summary>
    private const int StandingTailThreshold = 3000;

    /// <summary>
    /// 解出一个车次在各席别上的票面价。<b>同一席别出现多条记录时取第一条</b>
    /// （与官方一致：它念 <c>aria-label</c> 时也是命中第一条就返回）。
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

            if (!int.TryParse(record.Slice(CodeLength, JiaoLength), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var jiao))
                continue;

            if (!int.TryParse(record.Slice(6, 4), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var tail))
                continue;

            if (SeatCodeMap.IsUnknown(code))
                unknownCodes.Add(code);

            // 官方判无座只看尾巴，不看短码；照抄这个条件，不做"顺手改成按短码认"。
            var seatClass = tail >= StandingTailThreshold
                ? SeatClass.Standing
                : SeatCodeMap.SeatClassOf(code);
            if (seatClass is null) continue;

            // 首条优先：已存在即跳过后续同席别记录。
            result.TryAdd(seatClass.Value, jiao / 10m);
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
            foreach (var ch in fareString.AsSpan(offset + CodeLength, JiaoLength + 4))
            {
                if (!char.IsAsciiDigit(ch)) return false;
            }
        }
        return true;
    }
}
