using System.Text.Json;
using System.Text.Json.Nodes;
using RouteQuery.Data.Stations;

namespace RouteQuery.Tests;

/// <summary>
/// 测试用的真实样本与码表入口。样本一律从仓库的 <c>测试/接口样本/</c> 读取，
/// 不把官方响应硬编码进源码（SPEC-001 第五节：diff 可读性）。
/// </summary>
internal static class Samples
{
    private static readonly Lazy<string> SampleDir = new(() => FindSampleDir(AppContext.BaseDirectory));

    /// <summary>内置码表构建出的索引，全站 3404 条。测试因此测的是真数据而不是精心挑选的假数据。</summary>
    internal static readonly Lazy<StationIndex> Stations = new(() =>
        new StationIndex(StationTableLoader.LoadBundled()));

    /// <summary>读取一个样本文件的文本。</summary>
    internal static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(SampleDir.Value, fileName));

    /// <summary>读取并解析成 JSON 文档（返回原始文本由调用方自行反序列化，保留字段顺序可控性）。</summary>
    internal static string Json(string fileName) => Read(fileName);

    /// <summary>
    /// 组装一份形如官方响应的 JSON。用 <see cref="JsonObject"/> 显式拼装而不是字符串模板——
    /// 字符串模板里 JSON 的右花括号与内插语法会互相打架，为了转义去改写测试是本末倒置。
    /// </summary>
    internal static string Response(IEnumerable<string> records, IReadOnlyDictionary<string, string>? stationNames = null)
    {
        var resultArray = new JsonArray();
        foreach (var record in records) resultArray.Add((JsonNode?)JsonSerializer.SerializeToNode(record));

        var data = new JsonObject { ["result"] = resultArray };
        var map = new JsonObject();
        foreach (var (code, name) in stationNames ?? new Dictionary<string, string>())
            map[code] = name;
        data["map"] = map;
        data["flag"] = "1";
        data["level"] = "0";
        data["sametlc"] = "Y";

        return new JsonObject
        {
            ["status"] = true,
            ["httpstatus"] = 200,
            ["messages"] = new JsonArray(),
            ["data"] = data,
        }.ToJsonString();
    }

    /// <summary>
    /// 当前布局（v4，59 列）的跨车型真实样本。
    /// <para>旧的 58 列那份不删，但角色变了：它现在是<b>"列数漂移"的反例</b>——
    /// 用它必须解析失败，正是 <c>StructureDriftTests</c> 要的东西。
    /// 官方在 2026-10-20 与 10-28 之间把记录从 58 列改成 59 列（新列插在下标 57），
    /// 两份样本的逐位对照写在 v4 样本的 <c>_元信息</c> 里。</para>
    /// </summary>
    internal const string CrossKind = "leftTicket-跨车型样本-v4-59列-20261028.json";

    /// <summary>上一版布局（v3，58 列）的样本。只用于"列数不符必须不出数"这一类断言。</summary>
    internal const string CrossKindV3 = "leftTicket-跨车型样本.json";

    /// <summary>
    /// 从跨车型真实样本里取指定车次的那条原始竖线记录。
    /// 漂移守卫要在真记录上做定向破坏，因此这里返回未切分的原文。
    /// </summary>
    internal static string RecordOf(string trainCode)
    {
        using var doc = JsonDocument.Parse(Json(CrossKind));
        foreach (var e in doc.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
        {
            var text = e.GetString();
            if (text is null) continue;
            var cols = text.Split('|');
            if (cols.Length > 3 && cols[3] == trainCode) return text;
        }
        throw new InvalidOperationException(
            $"真实样本里没有车次 {trainCode}。样本是 2026-10-28 采集的，车次会随日期变化——需要重新采集样本，而不是改测试。");
    }

    private static string FindSampleDir(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "测试", "接口样本");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"未找到 测试/接口样本 目录（从 {start} 向上查找）。测试依赖真实样本，缺失时必须失败而不是跳过。");
    }

    /// <summary>
    /// 仓库根目录。守卫类测试要读**源码本身**（核对绑定名、找硬编码颜色、找定时器），
    /// 而测试输出目录里只有编译产物，所以只能从可执行文件所在目录往上找。
    /// </summary>
    internal static string RepoRoot { get; } = FindRepoRoot(AppContext.BaseDirectory);

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"从 {start} 往上找不到仓库根（判据：同时存在 src/ 与 AGENTS.md）。");
    }
}
