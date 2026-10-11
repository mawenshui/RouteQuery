namespace RouteQuery.Core.Ports;

/// <summary>
/// 版本发布源（FR-36）。这是 SPEC-007 六.1 里唯一被允许的第三方出站，边界写死在实现方。
/// <para>接口只给"最新版本号"和"发布页地址"两项，且<b>取不到时返回 null 而不是抛</b>：
/// 检查更新失败如果变成屏幕上一句错误提示，亲友读到的是"这个应用坏了"，而实际只是没网。
/// 需求把这一条列为硬性要求（FR-36 失败处理）。</para>
/// </summary>
public interface IReleaseFeed
{
    /// <summary>最新发布版本号，已去掉 v 前缀（形如 <c>0.3.0</c>）。任何失败都是 null。</summary>
    Task<string?> FetchLatestVersionAsync(CancellationToken ct);

    /// <summary>发布页地址。由实现方的仓库常量拼出，<b>不取自响应内容</b>——
    /// 响应是外部输入，不该变成"用系统浏览器打开"的参数。</summary>
    string ReleasePageUrl { get; }
}
