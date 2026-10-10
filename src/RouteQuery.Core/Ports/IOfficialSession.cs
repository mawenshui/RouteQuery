namespace RouteQuery.Core.Ports;

/// <summary>
/// 官方登录态。对应 FR-24 / FR-25 与 SPEC-007 第三节。
/// <para><b>这个接口刻意只有"写"和"清"，没有任何一个成员能把 Cookie 读回来。</b>
/// 这不是洁癖：本应用最大的合规风险是"程序替用户持有账号"，而越界的第一步通常就是
/// 某个地方需要"把 Cookie 拿出来看一眼"。读的路径留在数据层内部（只用于附带请求），
/// 端口层拿不到，任何新功能想用它都得先改这个接口——那是评审必然看到的改动。</para>
/// <para>同样地，这里不存在 <c>Login(user, password)</c> 之类的方法可以写：
/// 凭据只由用户本人在官方页面内输入（AGENTS 第五节第 2 条）。</para>
/// </summary>
public interface IOfficialSession
{
    /// <summary>本机是否持有一份登录态。只回答有/无，不返回内容。</summary>
    bool HasValidSession { get; }

    /// <summary>
    /// 收下用户在 WebView2 官方页面登录成功后由容器给出的 Cookie 头，加密存本机。
    /// <para>调用方必须是"用户刚刚在官方页面完成登录"这一事件，而不是任何自动时机。</para>
    /// </summary>
    void AdoptFromLoginPage(string cookieHeader);

    /// <summary>清除本机副本。WebView 容器侧的 Cookie 由宿主自己清，两处都要清才算退出（SPEC-007 三.4）。</summary>
    void Clear();
}
