using RouteQuery.Data.Http;

namespace RouteQuery.Tests;

/// <summary>
/// 登录态存储测试。它守的是本项目最重的一条责任：<b>应用会持有亲友本人的账号会话</b>。
/// 所以断言的重点不是"能不能存回去"，而是"明文会不会出现在不该出现的地方"。
/// </summary>
public class SessionStoreTests : IDisposable
{
    /// <summary>一条长得像真的的 Cookie 头。用可搜索的哨兵值，方便在文件里找明文。</summary>
    private const string CookieHeader = "BIGipServerotn=SENTINEL-VALUE-9f3a; JSESSIONID=ABCDEF0123456789; route=9f3a2b1c";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rq-session-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { /* 临时文件清不掉不影响结论 */ }
    }

    private ProtectedSessionStore NewStore() => new(_path);

    [Fact]
    public void 磁盘上找不到任何明文Cookie()
    {
        NewStore().AdoptFromLoginPage(CookieHeader);

        var raw = File.ReadAllText(_path);
        Assert.DoesNotContain("SENTINEL-VALUE-9f3a", raw);
        Assert.DoesNotContain("JSESSIONID", raw);
        Assert.DoesNotContain("BIGipServerotn", raw);
        Assert.DoesNotContain("ABCDEF0123456789", raw);
    }

    [Fact]
    public void 内部能取回原值且端口上没有读取成员()
    {
        var store = NewStore();
        store.AdoptFromLoginPage(CookieHeader);

        Assert.Equal(CookieHeader, store.ReadCookieHeaderForRequest());

        // 结构约束比行为约束更耐用：端口上一旦出现"返回字符串或集合"的方法，
        // 那就是 Cookie 外流的形状，评审与这条测试会同时拦住它。
        var leaking = typeof(Core.Ports.IOfficialSession).GetMethods()
            .Where(m => m.ReturnType == typeof(string)
                        || m.ReturnType == typeof(string[])
                        || m.ReturnType.IsGenericType
                        && m.ReturnType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            .Select(m => m.Name)
            .ToList();
        Assert.Empty(leaking);
    }

    [Fact]
    public void 清除之后回到未登录态()
    {
        var store = NewStore();
        store.AdoptFromLoginPage(CookieHeader);
        Assert.True(store.HasValidSession);

        store.Clear();

        Assert.False(store.HasValidSession);
        Assert.Null(store.ReadCookieHeaderForRequest());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void 空Cookie不被当作登录成功()
    {
        // 官方页面没给 Cookie 时保留原状——把"没拿到"记成"已登录"会让界面显示假状态。
        var store = NewStore();
        store.AdoptFromLoginPage("   ");

        Assert.False(store.HasValidSession);
        Assert.Null(store.SavedAt);
    }

    [Fact]
    public void 文件损坏或来自别的机器时判为无会话而不是崩溃()
    {
        File.WriteAllText(_path, "不是 base64，也不是我们写的东西");
        Assert.False(NewStore().HasValidSession);

        // 合法 base64 但不是本机 DPAPI 能解的（等价于从别人机器拷来的文件）
        File.WriteAllText(_path, Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
        Assert.False(NewStore().HasValidSession);
    }

    [Fact]
    public void 重复登录覆盖旧会话且只留一份()
    {
        var store = NewStore();
        store.AdoptFromLoginPage(CookieHeader);
        store.AdoptFromLoginPage("JSESSIONID=NEW-VALUE");

        Assert.Equal("JSESSIONID=NEW-VALUE", store.ReadCookieHeaderForRequest());
        var raw = File.ReadAllText(_path);
        Assert.DoesNotContain("SENTINEL-VALUE-9f3a", raw);   // 旧值不留痕
    }
}
