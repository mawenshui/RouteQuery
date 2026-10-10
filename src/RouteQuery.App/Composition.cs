using RouteQuery.App.Text;
using RouteQuery.Core.Model;
using RouteQuery.App.ViewModels;
using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;
using RouteQuery.Data.Parsing;
using RouteQuery.Data.Services;
using RouteQuery.Data.Stations;

namespace RouteQuery.App;

/// <summary>
/// 组合根。<b>本文件是 App 项目里唯一允许知道 Data 层具体类型的地方</b>——
/// ViewModel 与 View 一律只依赖 <see cref="Core.Ports"/> 里的接口。
/// <para>没有引入 DI 容器：整个应用的对象图是一棵固定的树，手写比配置容器更清楚，
/// 也少一个依赖。新增服务时在这里加一行即可。</para>
/// </summary>
public static class Composition
{
    /// <summary>设置存储。入口在 <see cref="App"/> 里要用它记"首次声明已确认"，
    /// 但 App 不得看见 Data 的具体类型，所以由组合根把接口递出去。</summary>
    public static ISettingsStore BuildSettings() => new SettingsStore();

    /// <summary>未处理异常落盘。日志路径属于数据层，App 侧不允许出现路径常量（SPEC-003 目录规则）。
    /// 写失败一律吞掉——崩溃提示本身比日志重要。</summary>
    public static void LogCrash(Exception ex)
    {
        try
        {
            System.IO.File.AppendAllText(
                Data.Store.AppPaths.TodayLog(),
                $"{DateTime.Now:O} 未处理异常 {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 故意忽略
        }
    }

    public static MainViewModel Build()    {
        var text = new ResourceTextProvider();

        var stations = BuildStations(text);
        var endpoints = new EndpointResolver();
        var client = new OfficialClient();
        var gate = new RequestGate(new SystemGateClock(), RequestGateOptions.Default);
        var parser = new LeftTicketParser(stations);

        var query = new TrainQueryService(client, gate, parser, endpoints);
        var stops = new TrainStopService(client, gate);
        var extension = new ExtensionQueryService(stops, query, gate, stations);
        var settings = new SettingsStore();

        return new MainViewModel(stations, query, stops, extension, settings, new QueryBudgetAdapter(gate), text,
            new OfficialLinkProvider());
    }

    /// <summary>
    /// 码表加载。<b>内置副本读不出来时必须让应用继续启动</b>——只是禁掉查询并说明原因，
    /// 而不是抛异常闪退（BF-03）。
    /// </summary>
    private static IStationRepository BuildStations(ITextProvider text)
    {
        try
        {
            return new StationIndex(StationTableLoader.LoadBundled());
        }
        catch (Exception)
        {
            return new UnavailableStationRepository(text.Get("错误_站点数据缺失"));
        }
    }

    /// <summary>码表不可用时的替身：一切查找都失败，从而让本地校验拦住请求而不是发出无效查询。</summary>
    private sealed class UnavailableStationRepository(string reason) : IStationRepository
    {
        public string Reason { get; } = reason;
        public DateOnly SourceDate => DateOnly.MinValue;
        public bool IsLoaded => false;
        public int Count => 0;
        public IReadOnlyList<Station> Search(string keyword, int take = 12) => [];
        public Station? FindByTelecode(string telecode) => null;
        public Station? FindByName(string name) => null;
    }
}
