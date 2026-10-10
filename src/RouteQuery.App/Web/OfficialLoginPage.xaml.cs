using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.Web;

/// <summary>
/// 官方登录页宿主。<b>WebView2 在本应用里只有一个用途</b>：把 12306 官方登录页原样呈现给
/// 屏幕前的用户本人，让他自己输入账号、密码与验证码（AGENTS 第五节第 2 条）。
/// <para>它刻意<b>不</b>做的事：不接收任何凭据参数、不代填任何表单、不识别验证码、
/// 不自动判定"登录成功了"。最后这条尤其要紧——官方登录成功的标志字段我们没有权威依据，
/// 猜一个就等于在凭据路径里塞进一个不可见的判断。所以改成用户自己点"我登录好了"，
/// 我们只在那一刻从容器里取 Cookie。</para>
/// </summary>
public partial class OfficialLoginPage : Window
{
    private readonly IOfficialSession _session;
    private readonly ITextProvider _text;
    private readonly IReadOnlyList<string> _cookieScopes;

    /// <param name="startUrl">登录页地址。由数据层给出，App 不拼 URL（AGENTS 第六节）。</param>
    public OfficialLoginPage(string startUrl, IOfficialSession session, IReadOnlyList<string> cookieScopes)
    {
        InitializeComponent();
        _session = session;
        _text = Composition.Text;
        _cookieScopes = cookieScopes;

        Loaded += async (_, _) => await StartAsync(startUrl);
    }

    private async Task StartAsync(string startUrl)
    {
        // 运行时是否可用：纯净 Win10 可能没装（Q-08 是"知情接受风险"项，没实测过）。
        // 这里给人话引导，不给异常类型名。
        try
        {
            if (string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()))
                throw new InvalidOperationException();
        }
        catch (Exception)
        {
            ShowMissingRuntime();
            return;
        }

        try
        {
            Directory.CreateDirectory(Composition.LoginPageProfileDirectory);
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: Composition.LoginPageProfileDirectory);
            await Web.EnsureCoreWebView2Async(env);
        }
        catch (Exception)
        {
            ShowMissingRuntime();
            return;
        }

        var core = Web.CoreWebView2;

        // 新窗口一律拒绝：官方页面里 target=_blank 很多，让它开新窗等于开出一个没有地址栏、
        // 也没有下面这套白名单检查的浏览器。
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Block(DomainGuard.HostLabel(e.Uri));
        };

        core.NavigationStarting += (_, e) =>
        {
            if (DomainGuard.IsAllowed(e.Uri))
            {
                BlockedBar.Visibility = Visibility.Collapsed;
                return;
            }

            e.Cancel = true;
            Block(DomainGuard.HostLabel(e.Uri));
        };

        // 地址文本一律从容器读，不由我们拼——DEC-10 的诚实性全押在这一点上。
        core.NavigationCompleted += (_, _) => HostLabel.Text = DomainGuard.HostLabel(core.Source);

        core.Navigate(startUrl);
    }

    /// <summary>用户自报"登录好了"。此刻才取 Cookie，不做任何自动判定。</summary>
    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2 is not { } core) return;

        // 逐站取再按名字合并：漏掉任何一个官方主机，都可能把登录身份那一条留在原地。
        var picked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in _cookieScopes)
            foreach (var c in await core.CookieManager.GetCookiesAsync(scope))
                picked[c.Name] = c.Value;

        if (picked.Count == 0)
        {
            StatusLine.Text = _text.Get("登录_未取到Cookie");
            return;
        }

        _session.AdoptFromLoginPage(string.Join("; ", picked.Select(kv => $"{kv.Key}={kv.Value}")));

        // 只报"拿到几条"，不报内容。够用户核对了，内容一个字都不该出现在界面上。
        StatusLine.Text = string.Format(_text.Get("登录_已保存"), picked.Count);
    }

    /// <summary>被白名单拦下时，允许用户改用系统浏览器——那里有真正的地址栏可以核对域名。</summary>
    private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.Source is not { Length: > 0 } url) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            StatusLine.Text = _text.Get("登录_已交浏览器");
        }
        catch (Exception)
        {
            StatusLine.Text = _text.Get("跳转_失败");
        }
    }

    private void ShowMissingRuntime()
    {
        Web.Visibility = Visibility.Collapsed;
        MissingRuntime.Visibility = Visibility.Visible;
    }

    private void Block(string host)
    {
        BlockedText.Text = string.Format(_text.Get("登录_被阻断"), host);
        BlockedBar.Visibility = Visibility.Visible;
    }
}
