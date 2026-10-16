using System.Text.Json;

namespace RouteQuery.Data.Parsing;

/// <summary>
/// 官方响应取子属性的安全入口。
/// <para><b>为什么需要它</b>：<see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>
/// 只对 <b>对象</b> 合法——在一个字符串或数组上调用它会抛 <c>InvalidOperationException</c>，
/// 而这个异常不是 <see cref="Core.Errors.QueryException"/>，会一路冒到 WPF 分发器上，
/// 界面连一句降级话都拿不到。2026-10-14 的日志里中转就是这么崩了三次
/// （<c>… requires an element of type 'Object', but the target element has type 'String'</c>），
/// 而余票与经停站的解析器里是同一形状的隐患。</para>
/// <para><b>用法是一条固定的顺序</b>：先 <see cref="IsObject"/> 问外层，再逐层 <see cref="Prop"/> 取子项，
/// 取到之后<b>再问一次它是不是对象</b>才继续往里走。三层各判什么，见任一调用点的注释——
/// "外层根本不是对象"与"这一项没给"是两件事，AGENTS 七.6 不许把它们合并。</para>
/// </summary>
internal static class JsonShape
{
    /// <summary>父元素本身是不是对象。<b>先问这个再取属性</b>，别指望 TryGetProperty 自己会客气。</summary>
    internal static bool IsObject(JsonElement el) => el.ValueKind == JsonValueKind.Object;

    /// <summary>安全地取一个子属性：父元素不是对象时不抛，一律当作"取不到"。</summary>
    internal static bool Prop(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value);
    }
}
