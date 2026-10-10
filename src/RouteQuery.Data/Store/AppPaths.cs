namespace RouteQuery.Data.Store;

/// <summary>
/// 本机数据目录。全部落在 <c>%LOCALAPPDATA%</c> 下，不含漫游profile，
/// 因为里面可能有加密后的官方会话（NFR-14：只存本机）。
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "RouteQuery";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string Stations => Ensure(Path.Combine(Root, "stations.json"));
    public static string Settings => Ensure(Path.Combine(Root, "settings.json"));
    public static string Endpoints => Ensure(Path.Combine(Root, "endpoints.json"));
    public static string Session => Ensure(Path.Combine(Root, "session.protected"));
    public static string DailyCount => Ensure(Path.Combine(Root, "daily.json"));
    public static string SavedRoutes => Ensure(Path.Combine(Root, "routes.json"));
    public static string History => Ensure(Path.Combine(Root, "history.json"));

    /// <summary>WebView2 的私有配置目录。只放官方登录页产生的缓存与 Cookie，
    /// 不碰用户自己的浏览器；"退出并清除"就是删这个目录（SPEC-007 三.4）。</summary>
    public static string WebViewProfile => Path.Combine(Root, "webview2");
    public static string Logs => EnsureDir(Path.Combine(Root, "logs"));

    /// <summary>日志文件按天一份，供亲友出问题时把文本发回来定位（FR-23）。</summary>
    public static string TodayLog() => Path.Combine(Logs, $"query-{DateTime.Now:yyyyMMdd}.log");

    private static string Ensure(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        return file;
    }

    private static string EnsureDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
