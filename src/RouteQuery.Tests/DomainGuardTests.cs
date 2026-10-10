using RouteQuery.App.Web;

namespace RouteQuery.Tests;

/// <summary>
/// 域名白名单测试。这一屏是本项目唯一一道"用户在应用里看到的到底是不是真 12306"的防线，
/// 所以它的判据必须逐条钉住——尤其是几种<b>看起来像官方</b>的写法。
/// </summary>
public class DomainGuardTests
{
    [Theory]
    [InlineData("https://kyfw.12306.cn/otn/leftTicket/init", true)]
    [InlineData("https://12306.cn/", true)]
    [InlineData("https://example.12306.cn/x", true)]
    [InlineData("https://KYFW.12306.cn/otn/passport", true)]          // 主机名大小写不敏感
    [InlineData("https://kyfw.12306.cn:443/otn/leftTicket/init", true)]
    [InlineData("http://kyfw.12306.cn/otn/leftTicket/init", false)]   // 明文一律不放行
    [InlineData("https://12306.cn.evil.com/", false)]                 // 后缀伪装
    [InlineData("https://evil12306.cn/", false)]                      // 前缀粘连
    [InlineData("https://user@kyfw.12306.cn/", false)]               // userinfo 里塞官方域名
    [InlineData("https://not12306.cn/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/x.html", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 只有官方域名的https地址被放行(string? url, bool expected) =>
        Assert.Equal(expected, DomainGuard.IsAllowed(url));

    [Fact]
    public void 显示用的主机名不来自我们自己的拼接()
    {
        // 地址栏文本必须是从容器读到的那个 URL 里取出的主机名；解析不了时明确说"无法识别"，
        // 而不是退回显示一个我们以为它应该是的地址。
        Assert.Equal("kyfw.12306.cn", DomainGuard.HostLabel("https://kyfw.12306.cn/otn/leftTicket/init"));

        // 被拦下时也要如实报出它本来要去哪——藏起主机名的警告等于让用户猜。
        Assert.Equal("12306.cn.evil.com", DomainGuard.HostLabel("https://12306.cn.evil.com/"));
        Assert.Equal("地址无法识别", DomainGuard.HostLabel("不是地址"));
    }

    [Fact]
    public void 子域判定要求有点分隔符()
    {
        // "not12306.cn" 以 "12306.cn" 结尾但不是它的子域——少了这个点，白名单就漏了。
        Assert.False(DomainGuard.IsAllowed("https://not12306.cn/"));
        Assert.True(DomainGuard.IsAllowed("https://a.b.12306.cn/"));
    }
}
