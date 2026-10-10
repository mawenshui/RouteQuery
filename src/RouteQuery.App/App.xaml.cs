using System.Windows;
using RouteQuery.App.Text;
using RouteQuery.Core.Ports;

namespace RouteQuery.App;

/// <summary>
/// 应用入口。两件事：<b>首次启动的非官方声明确认</b>（FR-20）与<b>全局异常兜底</b>——
/// 任何未预料的异常都转成人话并记进日志，而不是弹一个英文堆栈对话框，更不能让应用直接消失
/// （SPEC-004 四、AGENTS 七.3）。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            ShowCrash(args.Exception);
            args.Handled = true;   // 不让它把进程带走：亲友看到的会是"应用凭空消失"
        };

        var settings = Composition.BuildSettings();
        if (!AcknowledgeDisclaimer(settings))
        {
            // 不接受声明就不进入主界面。这是 P0 约束，不给"以后再说"的口子。
            Shutdown();
            return;
        }

        MainWindow = new MainWindow { DataContext = Composition.Build() };
        MainWindow.Show();
    }

    /// <summary>首次启动弹一次确认框；已确认过就直接放行（FR-20 验收②）。拒绝则返回 false。</summary>
    private static bool AcknowledgeDisclaimer(ISettingsStore settings)
    {
        var current = settings.Load();
        if (current.DisclaimerAccepted) return true;

        var text = new ResourceTextProvider();
        var accepted = MessageBox.Show(
                text.Get("声明_首次正文"),
                text.Get("声明_首次标题"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information) == MessageBoxResult.OK;

        if (accepted) settings.Save(current with { DisclaimerAccepted = true });
        return accepted;
    }

    private void ShowCrash(Exception ex)
    {
        Composition.LogCrash(ex);   // 落盘由组合根负责，App 不知道日志路径在哪

        var text = new ResourceTextProvider();
        MessageBox.Show(
            text.Get("错误_意外崩溃"),
            text.Get("崩溃_标题"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
