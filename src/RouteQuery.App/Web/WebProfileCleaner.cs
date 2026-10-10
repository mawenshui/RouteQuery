using System.IO;

namespace RouteQuery.App.Web;

/// <summary>
/// 清掉 WebView2 的私有配置目录。
/// <para>单独成类而不是挂在登录窗口上，是因为 ViewModel 也要调它（"退出并清除"按钮在设置区），
/// 而 <b>ViewModel 不允许依赖任何窗口类型</b>（SPEC-004 五.1）。</para>
/// <para>没有活的 WebView2 控件时，删目录是清容器 Cookie 的唯一办法——那里面只有本应用
/// 登录页产生的缓存与 Cookie，不碰用户自己的浏览器（SPEC-007 三.4 要求两处都清）。</para>
/// </summary>
public static class WebProfileCleaner
{
    public static void Clear()
    {
        try
        {
            if (Directory.Exists(Composition.LoginPageProfileDirectory))
                Directory.Delete(Composition.LoginPageProfileDirectory, recursive: true);
        }
        catch (Exception)
        {
            // 登录页还开着导致目录被占用时不抛：应用侧副本已清，下一个请求就是干净的。
        }
    }
}
