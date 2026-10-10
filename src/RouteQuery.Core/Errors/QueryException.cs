namespace RouteQuery.Core.Errors;

/// <summary>
/// 面向界面的查询异常。数据层负责把技术异常（<c>HttpRequestException</c> 等）映射成带
/// <see cref="QueryErrorKind"/> 的本类型；界面层只按 Kind 取文案，**禁止**看到原始异常类型
/// （SPEC-004 四、错误处理）。
/// </summary>
/// <param name="Kind">错误分类，决定界面文案与是否允许重试。</param>
/// <param name="Detail">供本地日志使用的技术细节；不得包含 Cookie 或账号信息。</param>
public sealed class QueryException(QueryErrorKind kind, string? detail = null) : Exception
{
    public QueryErrorKind Kind { get; } = kind;

    public string? Detail { get; } = detail;

    /// <summary>
    /// 该分类是否允许界面向用户提供"重试"按钮。
    /// <para>刻意排除 <see cref="QueryErrorKind.UpstreamRejected"/> 与
    /// <see cref="QueryErrorKind.ParseFailure"/>：前者重试等于在风控面前持续撞，
    /// 后者重试永远不会成功（SPEC-007 三.7、四）。</para>
    /// </summary>
    public bool AllowsUserRetry => Kind is QueryErrorKind.NetworkUnavailable or QueryErrorKind.Timeout;

    public static QueryException InputInvalid(string whichField) =>
        new(QueryErrorKind.InputInvalid, whichField);

    public static QueryException DataUnavailable(string what) =>
        new(QueryErrorKind.DataUnavailable, what);

    public static QueryException ParseFailure(string reason) =>
        new(QueryErrorKind.ParseFailure, reason);
}
