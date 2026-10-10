using RouteQuery.Data.Store;

namespace RouteQuery.Data.Http;

/// <summary>
/// 余票接口路径后缀的缓存。
/// <para>实测 <c>/otn/leftTicket/query</c> 会返回 302，Location 指向 <c>queryG?原参数</c>，
/// 而后缀历史上变过多次。因此后缀<b>不是常量</b>：首次按响应引导发现，之后缓存复用，
/// 缓存失效时自动重新发现一次（DEC-06）。</para>
/// </summary>
public sealed class EndpointResolver
{
    /// <summary>官方当前的入口路径名；发现成功后被替换成带后缀的实际路径名。</summary>
    private const string EntryName = "query";

    private readonly string _path;
    private string _suffixName = EntryName;

    public EndpointResolver(string? storePath = null)
    {
        _path = storePath ?? AppPaths.Endpoints;
        var saved = JsonFileStore.Read<EndpointDoc>(_path)?.LeftTicketSuffix;
        if (!string.IsNullOrWhiteSpace(saved) && !saved.Contains('/') && !saved.Contains('?'))
            _suffixName = saved;   // 缓存值只可能是路径末段，含分隔符即视为被外部改坏，忽略
    }

    /// <summary>当前使用的余票接口路径末段，如 <c>queryG</c>。</summary>
    public string LeftTicketName => _suffixName;

    /// <summary>记录新发现的后缀。校验失败时不写，避免把坏值持久化。</summary>
    public void SaveLeftTicketSuffix(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('?')) return;
        if (name == _suffixName) return;

        _suffixName = name;
        JsonFileStore.Write(_path, new EndpointDoc(name, DateTimeOffset.Now));
    }

    /// <summary>从 302 的 Location 里取出路径末段作为新后缀；取不出时返回 null。</summary>
    public static string? SuffixFrom(string location)
    {
        var head = location.Split('?', '#')[0];
        var last = head[(head.LastIndexOf('/') + 1)..];
        return last.Length == 0 ? null : last;
    }

    /// <summary>持久化的端点缓存。</summary>
    /// <param name="LeftTicketSuffix">上次发现到的余票接口路径末段。</param>
    /// <param name="DiscoveredAt">发现时间。过旧时宁可重新发现一次，也不长期沿用。</param>
    public sealed record EndpointDoc(string? LeftTicketSuffix, DateTimeOffset? DiscoveredAt);
}
