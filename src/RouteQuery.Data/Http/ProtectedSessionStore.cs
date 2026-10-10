using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Store;

namespace RouteQuery.Data.Http;

/// <summary>
/// 登录态的本机存储。对应 SPEC-007 三.3 / 三.4 / 三.5。
/// <para>三条硬规则：<b>①</b> 落盘前用 DPAPI（当前用户范围）加密，磁盘上永远看不到明文 Cookie；
/// <b>②</b> 不写日志、不进错误文本、不上传；<b>③</b> 读取方法是本程序集内部方法，
/// 端口 <see cref="IOfficialSession"/> 上没有读取成员——App 侧连"看一眼"的口子都没有。</para>
/// <para>关于"要不要再加一层应用自带的 entropy"：加了并不更安全（那把钥匙就编在 exe 里，
/// 能解密文件的人同样能读到它），却会让"换机器/重装后读不出来"变得难以解释。
/// 因此这里只用 DPAPI 的 CurrentUser 范围，并把真实边界写清楚：</para>
/// <para><b>同一 Windows 账户下的任何进程理论上都能解密本文件。</b>它防的是"文件被随手拷走、
/// 被同步盘带走、被另一个账户读到"，不防"攻击者已经以你的身份运行代码"——后者这台机器上
/// 没有任何本地文件是安全的。这条局限同时写进了使用说明。</para>
/// </summary>
public sealed class ProtectedSessionStore(string? path = null) : IOfficialSession
{
    private readonly object _sync = new();
    private readonly string _path = path ?? AppPaths.Session;

    /// <summary>落盘形态。带时间只为了在界面上说清"这份登录态是什么时候拿到的"。</summary>
    private sealed record Payload(string CookieHeader, DateTimeOffset SavedAt);

    public bool HasValidSession
    {
        get
        {
            lock (_sync) return ReadRaw() is not null;
        }
    }

    /// <summary>拿到登录态的时刻；没有会话时为 null。界面用它显示"登录于…"</summary>
    public DateTimeOffset? SavedAt
    {
        get
        {
            lock (_sync) return ReadRaw()?.SavedAt;
        }
    }

    public void AdoptFromLoginPage(string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            // 空值不当作"登录成功"。官方页面没给 Cookie 时保留原状，让用户看到真实状态。
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Payload(cookieHeader, DateTimeOffset.Now));
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);

        lock (_sync) JsonFileStore.WriteAtomic(_path, Convert.ToBase64String(protectedBytes));
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (!File.Exists(_path)) return;
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
                // 删不掉时按"无会话"处理：宁可让用户重新登录，也不带着一个删不掉的凭据继续跑。
                try { File.WriteAllText(_path, string.Empty); } catch (IOException) { /* 同上 */ }
            }
        }
    }

    /// <summary>
    /// 供 HTTP 客户端附带请求用。<b>internal 是刻意的</b>：App 与 Core 拿不到它，
    /// Cookie 值因此不会出现在"界面能调用的任何签名"里。
    /// </summary>
    internal string? ReadCookieHeaderForRequest()
    {
        lock (_sync) return ReadRaw()?.CookieHeader;
    }

    private Payload? ReadRaw()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var blob = File.ReadAllText(_path).Trim();
            if (blob.Length == 0) return null;

            var plain = ProtectedData.Unprotect(Convert.FromBase64String(blob), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Payload>(plain);
        }
        catch (Exception)
        {
            // 解密失败（换账户、文件被截断、格式变了）一律视为"没有会话"，
            // 而不是弹一句"凭据文件损坏"——那句话除了吓到人之外没有下一步可做。
            return null;
        }
    }
}
