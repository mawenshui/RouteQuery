namespace RouteQuery.Core.Errors;

/// <summary>
/// 查询失败的分类。八类各自对应不同的界面处置，**不得合并**
/// （AGENTS.md 七.6：用户必须能从提示里判断该等、该重试，还是该换新版本）。
/// </summary>
public enum QueryErrorKind
{
    /// <summary>输入不合法，在发请求之前就被本地校验拦下（FR-04）。</summary>
    InputInvalid,

    /// <summary>本机网络问题：DNS、连接、TLS 失败。允许用户重试一次。</summary>
    NetworkUnavailable,

    /// <summary>官方响应超过时限（FR-08 / NFR-01）。允许用户重试一次。</summary>
    Timeout,

    /// <summary>官方拒绝了请求（返回 HTML、异常页或非预期 302）。必须停止后续请求，不得重试。</summary>
    UpstreamRejected,

    /// <summary>需要官方登录会话才能拿到的数据当前无会话。界面显示引导态而不是错误态（FR-06）。</summary>
    SessionRequired,

    /// <summary>响应结构与列布局描述表不符，即官方改了字段。判为不可出数，提示需要新版本（RISK-01）。</summary>
    ParseFailure,

    /// <summary>官方正常响应，但该项数据为空——这是有效信息，渲染为空状态而非错误（FR-10）。</summary>
    NoResult,

    /// <summary>
    /// 官方接口可达但没有给出这一项数据（经停站数组为空、某席别无票价、字段缺失）。
    /// <para>与 <see cref="NoResult"/> 的区别：那是"官方告诉你没有这趟车"，这是"官方没给这个字段"。</para>
    /// <para>与 <see cref="ParseFailure"/> 的区别：那是结构变了需要新版本，这是结构正常但值为空，可以告知也可以重试。</para>
    /// 按 NFR-18，此情形**只能报错**，禁止用推断值或缓存填补。
    /// </summary>
    DataUnavailable,
}
