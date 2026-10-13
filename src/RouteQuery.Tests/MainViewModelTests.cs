using RouteQuery.App.ViewModels;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.Tests;

/// <summary>
/// 主视图模型的测试。对应 SPEC-005"界面层也要有可执行断言"那条。
/// <para>为什么值得单独立一份：第 2、3 轮那四处缺陷（一次成功的查询永远停在转圈上、选完出发站后
/// 到达站的候选弹层自己弹回来、改一次扩展上限抹掉别的设置、历史没落盘）
/// **没有一处是当时的单元测试挡住的**，全部靠实跑发现。而"实跑"在锁屏的日子做不了。</para>
/// <para>断言的都是用户能观察到的东西：状态、行数、用了哪条文案、发出了几次请求。
/// 不断言控件与颜色——那是界面走查的活。</para>
/// </summary>
public class MainViewModelTests
{
    private static readonly Station Beijing = new("北京南", "VNP", "beijingnan", "bjn", "1001", "北京");
    private static readonly Station Nanjing = new("南京南", "NJH", "nanjingnan", "njj", "1301", "南京");
    private static readonly Station Shanghai = new("上海虹桥", "AOH", "shanghaihongqiao", "shq", "0201", "上海");

    /// <summary>一套替身 + 一个装配好的视图模型。默认不自动检查更新，免得后台任务干扰计数。</summary>
    private sealed class Harness
    {
        public FakeSettings Settings { get; }
        public FakeQuery Query { get; } = new();
        public FakeTransfer Transfer { get; } = new();
        public FakeSession Session { get; } = new();
        public FakeBook Book { get; } = new();
        public FakeHistory History { get; } = new();
        public FakeStations Stations { get; } = new();
        public StopService Stops { get; } = new();
        public FakeRelease Release { get; } = new();
        public CopyText Text { get; } = new();
        public MainViewModel Vm { get; }

        public Harness(AppSettings? initial = null)
        {
            Settings = new FakeSettings(initial ?? new AppSettings(CheckUpdatesEnabled: false));
            Vm = new MainViewModel(Stations, Query, Stops, new ExtensionService(), Settings,
                new Budget(), Text, new Links(), Session, Book, History, new Updater(), Transfer, Release);
        }

        /// <summary>选好两站，走的就是界面那条提交路径：列表选中项 → 视图模型属性。</summary>
        public Harness Ready()
        {
            Vm.FromPick = Beijing;
            Vm.ToPick = Shanghai;
            return this;
        }

        public sealed class FakeSettings(AppSettings value) : ISettingsStore
        {
            public AppSettings Current = value;
            public int SaveCount;
            public AppSettings Load() => Current;
            public void Save(AppSettings settings) { Current = settings; SaveCount++; }
        }

        public sealed class FakeQuery : ITrainQueryService
        {
            public int Calls;
            public IReadOnlyList<TrainJourney> Result = [];
            public QueryException? Throws;
            public Action<CancellationToken>? OnCall;

            public Task<IReadOnlyList<TrainJourney>> SearchAsync(DirectQueryRequest request, CancellationToken ct = default)
            {
                Calls++;
                OnCall?.Invoke(ct);
                ct.ThrowIfCancellationRequested();   // 真实客户端会在这里把取消抛出来
                if (Throws is not null) throw Throws;
                return Task.FromResult(Result);
            }
        }

        public sealed class StopService : ITrainStopService
        {
            public int Calls;

            public Task<TrainRoute> GetRouteAsync(string trainNo, string fromTelecode, string toTelecode,
                DateOnly travelDate, CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(new TrainRoute(trainNo, "G1", [
                    new StopDetail("01", Beijing.Name, null, new TimeSpan(7, 0, 0), "始发", true, false),
                    new StopDetail("07", Nanjing.Name, new TimeSpan(10, 20, 0), new TimeSpan(10, 24, 0), "4分钟", false, false),
                    new StopDetail("13", Shanghai.Name, new TimeSpan(11, 24, 0), null, "终到", false, true)]));
            }
        }

        public sealed class ExtensionService : IExtensionQueryService
        {
            public int Calls;

            public Task<ExtensionBatch> RunAsync(TrainJourney target, DateOnly travelDate, int maxExtraStations,
                IProgress<ExtensionProgress>? progress = null, CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(new ExtensionBatch([], 0, ExtensionStopReason.Completed, 0));
            }
        }

        public sealed class FakeTransfer : ITransferQueryService
        {
            public int Calls;
            public TransferQueryResult Result = new([], 0, 0);
            public QueryException? Throws;

            public Task<TransferQueryResult> SearchAsync(Station from, Station to, DateOnly travelDate, CancellationToken ct = default)
            {
                Calls++;
                if (Throws is not null) throw Throws;
                return Task.FromResult(Result);
            }
        }

        public sealed class FakeSession : IOfficialSession
        {
            public bool HasValidSession { get; set; } = true;
            public int Clears;
            public DateTimeOffset? SavedAt => null;
            public void AdoptFromLoginPage(string cookieHeader) { }
            public void Clear() { HasValidSession = false; Clears++; }
        }

        public sealed class FakeBook : IRouteBook
        {
            public List<SavedRoute> Items = [];
            public bool Accepts { get; set; } = true;
            public IReadOnlyList<SavedRoute> All => Items;
            public bool Add(SavedRoute route) { if (!Accepts) return false; Items.Add(route); return true; }
            public bool Remove(string name) => Items.RemoveAll(r => r.Name == name) > 0;
        }

        public sealed class FakeHistory : IQueryHistory
        {
            public List<QueryHistoryEntry> Items = [];
            public IReadOnlyList<QueryHistoryEntry> All => Items;
            public void Record(QueryHistoryEntry entry) => Items.Add(entry);
            public void Clear() => Items.Clear();
        }

        public sealed class FakeStations : IStationRepository
        {
            public bool IsLoaded { get; set; } = true;
            public DateOnly SourceDate => new(2026, 10, 10);
            public int Count => 3404;

            public IReadOnlyList<Station> Search(string keyword, int take = 12) =>
                keyword.Length < 2 ? [] : AllStations.Where(s => s.Name.Contains(keyword)).Take(take).ToList();

            public Station? FindByTelecode(string telecode) => AllStations.FirstOrDefault(s => s.Telecode == telecode);
            public Station? FindByName(string name) => AllStations.FirstOrDefault(s => s.Name == name);

            private static readonly Station[] AllStations = [Beijing, Nanjing, Shanghai];
        }

        public sealed class FakeRelease : IReleaseFeed
        {
            public string? Latest;
            public int Calls;
            public string ReleasePageUrl => "https://github.com/mawenshui/RouteQuery/releases/latest";

            public Task<string?> FetchLatestVersionAsync(CancellationToken ct)
            {
                Calls++;
                return Task.FromResult(Latest);
            }
        }

        public sealed class Budget : IQueryBudget
        {
            public int DailyRemaining => 197;
        }

        public sealed class Updater : IStationTableUpdater
        {
            public Task<StationTableUpdateResult> UpdateAsync(CancellationToken ct = default) =>
                Task.FromResult(new StationTableUpdateResult(true, 3404, new DateOnly(2026, 10, 12), null));
        }

        public sealed class Links : IOfficialLinkProvider
        {
            public string LeftTicketPageUrl => "https://kyfw.12306.cn/otn/leftTicket/init";
            public string LoginPageUrl => "https://kyfw.12306.cn/otn/passport?redirect=/otn/leftTicket/init";
            public IReadOnlyList<string> CookieScopeUrls => ["https://kyfw.12306.cn/"];
        }

        /// <summary>默认回键名本身：断言"用了哪条文案"，内容归界面走查。
        /// 个别判据要看"参数有没有落到句子里"（如版本号），给它挂一个映射就行。</summary>
        public sealed class CopyText : ITextProvider
        {
            public Func<string, string>? Map { get; set; }
            public string Get(string key) => Map?.Invoke(key) ?? key;
        }
    }

    private static TrainJourney Journey(string code, decimal? price = 553.00m, SeatState state = SeatState.Available) => new(
        code, $"240000{code}0L", Beijing, Shanghai, Beijing, Shanghai,
        new TimeSpan(7, 0, 0), new TimeSpan(11, 24, 0), new TimeSpan(4, 24, 0),
        false, PurchaseState.Buyable,
        [new SeatAvailability(SeatClass.SecondClass, state == SeatState.Available ? "有" : "无", state, price)],
        null);

    // ── 状态机：四处实跑缺陷的回归 ──────────────────────────────

    [Fact]
    public async Task 一次成功的查询不会停在加载态()
    {
        // 当年那个 bug：ApplyLocalView 对"还在加载"直接 return，顺序写反时数据到手也永远转圈。
        var h = new Harness();
        h.Query.Result = [Journey("G1"), Journey("G3")];
        h.Ready();

        await h.Vm.QueryAsync(default);

        Assert.Equal(ViewState.Results, h.Vm.State);
        Assert.Equal(2, h.Vm.Rows.Count);
        Assert.False(h.Vm.QueryCommand.IsRunning);
    }

    [Fact]
    public async Task 成功的查询会记进历史并带上结果条数()
    {
        var h = new Harness();
        h.Query.Result = [Journey("G1"), Journey("G3"), Journey("G5")];
        h.Ready();

        await h.Vm.QueryAsync(default);

        var entry = Assert.Single(h.History.Items);
        Assert.Equal(3, entry.ResultCount);
        Assert.Equal("VNP", entry.FromTelecode);
        Assert.Single(h.Vm.History);   // 列表里也要立刻看得见，不用重开程序
    }

    [Fact]
    public async Task 失败的查询不占历史位置()
    {
        var h = new Harness();
        h.Query.Throws = new QueryException(QueryErrorKind.Timeout);
        h.Ready();

        await h.Vm.QueryAsync(default);

        Assert.Empty(h.History.Items);
        Assert.Empty(h.Vm.History);
        Assert.Equal(ViewState.Error, h.Vm.State);
    }

    [Fact]
    public void 改扩展上限不会抹掉别的设置()
    {
        // 原写法是整体覆盖：改一次站数就把"已看过声明"清回 false，下次启动又弹一遍。
        var h = new Harness(new AppSettings(MaxExtensionStations: 3, DisclaimerAccepted: true,
            CheckUpdatesEnabled: false, Theme: ThemeChoice.Dark));

        h.Vm.MaxExtensionStations = 7;

        Assert.True(h.Settings.Current.DisclaimerAccepted);
        Assert.False(h.Settings.Current.CheckUpdatesEnabled);
        Assert.Equal(ThemeChoice.Dark, h.Settings.Current.Theme);
        Assert.Equal(7, h.Settings.Current.MaxExtensionStations);
    }

    [Fact]
    public async Task 打字会作废已选站而选中才提交()
    {
        // DEC-17：提交只有一条路径（列表选中项 → 视图模型属性）。
        // 这条测试守的是它成立的原因——**打字会把已选站清掉**，所以"带着没选完的弹层去查询"
        // 会被本地校验拦下，弹层根本没有机会盖在结果上。
        var h = new Harness();
        h.Ready();

        h.Vm.FromText = "北京";

        Assert.NotEmpty(h.Vm.FromCandidates);
        Assert.Empty(h.Vm.Rows);

        await h.Vm.QueryAsync(default);

        Assert.Equal(0, h.Query.Calls);          // 被本地校验拦下，一个请求都不发
        Assert.Equal(ViewState.Error, h.Vm.State);

        h.Vm.FromPick = Beijing;                 // 用户从弹层里选一条
        h.Vm.ToPick = Shanghai;
        Assert.Empty(h.Vm.FromCandidates);       // 选完立刻收起
        Assert.Empty(h.Vm.ToCandidates);

        h.Query.Result = [Journey("G1")];
        await h.Vm.QueryAsync(default);
        Assert.Equal(1, h.Query.Calls);
        Assert.Equal(ViewState.Results, h.Vm.State);
    }

    // ── 本地校验与频率红线 ─────────────────────────────────────

    [Fact]
    public async Task 没选站时一个请求都不发()
    {
        var h = new Harness();

        await h.Vm.QueryAsync(default);

        Assert.Equal(0, h.Query.Calls);
        Assert.Equal(ViewState.Error, h.Vm.State);
        Assert.False(h.Vm.CanRetry);   // 这种错重试没有意义，用户要做的是选站

        // FR-04 给每种非法输入写了各自的说法，还要"输入框标红"。共用一句"输入无效"时
        // 用户读完仍然不知道动哪个框，所以这三条断言守的是措辞而不是流程。
        Assert.Equal("错误_未选出发站", h.Vm.ErrorText);
        Assert.True(h.Vm.FromInvalid);
        Assert.True(h.Vm.ToInvalid);   // 两个都空，两个都该标红
    }

    [Fact]
    public async Task 只缺到达站时说的是到达站而不是笼统一句()
    {
        var h = new Harness();
        h.Vm.FromPick = Beijing;

        await h.Vm.QueryAsync(default);

        Assert.Equal(0, h.Query.Calls);
        Assert.Equal("错误_未选到达站", h.Vm.ErrorText);
        Assert.False(h.Vm.FromInvalid);
        Assert.True(h.Vm.ToInvalid);
    }

    [Fact]
    public async Task 红框在用户改选之后消失()
    {
        // 红框的意义是"这里需要你动一下"。选完还红着，就是在指责一个已经修好的东西。
        var h = new Harness();
        await h.Vm.QueryAsync(default);
        Assert.True(h.Vm.FromInvalid);

        h.Vm.FromPick = Beijing;
        Assert.False(h.Vm.FromInvalid);

        h.Vm.ToPick = Shanghai;
        Assert.False(h.Vm.ToInvalid);
    }

    [Fact]
    public async Task 中转与收藏说的是同一句()
    {
        // 三个入口（直达、中转、收藏）各自校验一次。措辞分叉的话，用户会以为有三种不同的毛病。
        var h = new Harness();

        await h.Vm.RunTransferAsync(default);
        Assert.Equal("错误_未选出发站", h.Vm.TransferStatus);

        h.Vm.SaveCurrentRoute();
        Assert.Equal("错误_未选出发站", h.Vm.BookStatus);
    }

    [Fact]
    public async Task 两站相同时不发请求()
    {
        // TC-A8 六项非法输入之一：官方对同站 OD 会返回一个看不懂的页面，本地就该拦下。
        var h = new Harness();
        h.Vm.FromPick = Beijing;
        h.Vm.ToPick = Beijing;

        await h.Vm.QueryAsync(default);

        Assert.Equal(0, h.Query.Calls);
        Assert.Equal(ViewState.Error, h.Vm.State);
        Assert.Equal("错误_两站相同", h.Vm.ErrorText);
        // 两个框一起红：问题在"这一对"上，只红一个会把人引向错误的那个框。
        Assert.True(h.Vm.FromInvalid);
        Assert.True(h.Vm.ToInvalid);
    }

    [Fact]
    public void 扩展站数越界被钳到边界而不是照用()
    {
        // TC-D6 / FR-29 验收③：配置或输入被改成界面上没有的值时，钳制发生在代码层。
        var h = new Harness();

        h.Vm.MaxExtensionStations = 0;
        var low = h.Vm.MaxExtensionStations;
        h.Vm.MaxExtensionStations = 11;
        var high = h.Vm.MaxExtensionStations;

        Assert.Equal(AppSettings.MinStations, low);
        Assert.Equal(AppSettings.MaxStations, high);
        Assert.Equal(AppSettings.MaxStations, h.Settings.Current.MaxExtensionStations);
    }

    [Fact]
    public async Task 码表不可用时禁用查询并说明原因()
    {
        // BF-03：内置码表读不出来时应用要继续跑，只是不能发查询。
        var h = new Harness();
        h.Stations.IsLoaded = false;
        h.Ready();

        await h.Vm.QueryAsync(default);

        Assert.Equal(0, h.Query.Calls);
        Assert.Equal(ViewState.Error, h.Vm.State);
    }

    [Fact]
    public async Task 官方拒绝时不给重试按钮而超时给()
    {
        // AGENTS 五.6：疑似风控时唯一正确处理是停止并让用户稍后再试，不能"换个姿势再点一次"。
        var rejected = new Harness();
        rejected.Query.Throws = new QueryException(QueryErrorKind.UpstreamRejected);
        rejected.Ready();
        await rejected.Vm.QueryAsync(default);
        Assert.False(rejected.Vm.CanRetry);

        var timeout = new Harness();
        timeout.Query.Throws = new QueryException(QueryErrorKind.Timeout);
        timeout.Ready();
        await timeout.Vm.QueryAsync(default);
        Assert.True(timeout.Vm.CanRetry);
    }

    [Fact]
    public async Task 官方说没有和查不出是两种说法()
    {
        // NoResult / DataUnavailable / ParseFailure 不得合并（AGENTS 七.6）。
        var none = new Harness();
        none.Query.Result = [];
        none.Ready();
        await none.Vm.QueryAsync(default);
        Assert.NotEqual(ViewState.Error, none.Vm.State);
        Assert.Equal("状态_无结果", none.Vm.StatusText);

        var unavailable = new Harness();
        unavailable.Query.Throws = new QueryException(QueryErrorKind.DataUnavailable);
        unavailable.Ready();
        await unavailable.Vm.QueryAsync(default);
        Assert.Equal(ViewState.Error, unavailable.Vm.State);
        Assert.NotEqual("状态_无结果", unavailable.Vm.StatusText);
    }

    [Fact]
    public async Task 取消查询后回到可继续的状态而不是错误()
    {
        var h = new Harness();
        h.Query.Result = [Journey("G1")];
        h.Ready();

        using var cts = new CancellationTokenSource();
        h.Query.OnCall = _ => cts.Cancel();

        await h.Vm.QueryAsync(cts.Token);

        Assert.NotEqual(ViewState.Error, h.Vm.State);
        Assert.Equal("错误_已取消", h.Vm.StatusText);
    }

    [Fact]
    public async Task 筛没了和官方没有是两种说法()
    {
        // AGENTS 七.6：NoResult 是官方给的信息，"筛没了"是我们自己过滤的结果，混起来会让人白等。
        var h = new Harness();
        h.Query.Result = [Journey("G1", state: SeatState.SoldOut)];
        h.Ready();
        await h.Vm.QueryAsync(default);
        Assert.Equal(ViewState.Results, h.Vm.State);

        h.Vm.OnlyAvailable = true;

        Assert.Equal(ViewState.FilteredEmpty, h.Vm.State);
        Assert.Equal("状态_筛选后空", h.Vm.StatusText);
    }

    [Fact]
    public async Task 互换两站保留日期与筛选而结果区回到未查询()
    {
        // TC-A6：换向是一个准备动作，不该顺手把用户的筛选条件清光，也不该报一个错。
        var h = new Harness();
        h.Query.Result = [Journey("G1")];
        h.Ready();
        h.Vm.OnlyAvailable = true;
        await h.Vm.QueryAsync(default);

        h.Vm.Swap();

        Assert.Equal("上海虹桥", h.Vm.FromText);
        Assert.Equal("北京南", h.Vm.ToText);
        Assert.True(h.Vm.OnlyAvailable);
        Assert.Equal(ViewState.Empty, h.Vm.State);   // 列表因此隐藏：看到的是"还没查询"，不是错误
        Assert.Equal(1, h.Query.Calls);              // 换向本身不发请求，等用户点查询
    }

    // ── 中转与登录态（FR-06 / FR-24 / FR-25）─────────────────────

    [Fact]
    public async Task 未登录时中转是引导态而不是报错()
    {
        var h = new Harness();
        h.Session.HasValidSession = false;
        h.Ready();

        await h.Vm.RunTransferAsync(default);

        Assert.Equal("中转_需登录", h.Vm.TransferStatus);
        Assert.Equal(0, h.Transfer.Calls);   // 引导态不发请求
    }

    [Fact]
    public async Task 官方不认会话时本地副本立刻清掉并回到引导()
    {
        // 设计 §6.2"失效即清除"：留一份官方不认的会话，等于让亲友一直点一个不会成功的按钮。
        var h = new Harness();
        h.Transfer.Throws = new QueryException(QueryErrorKind.SessionRequired);
        h.Ready();

        await h.Vm.RunTransferAsync(default);

        // 视图模型这边只负责"回到引导态"；真正清掉本机副本的是数据层的服务（设计 §6.2），
        // 那里需要真闸门与真客户端才能测，已记进验收报告的"未自动化"项。
        Assert.Equal("中转_会话失效", h.Vm.TransferStatus);
        Assert.NotEqual(ViewState.Error, h.Vm.State);   // 引导态不该吓人
        Assert.Equal(1, h.Transfer.Calls);
    }

    [Fact]
    public async Task 中转出数时按官方推荐顺序如实报数()
    {
        var plan = new TransferPlan(
            new TransferLeg("G25", "240000G250L", Beijing, Nanjing, new TimeOnly(17, 0), new TimeOnly(20, 14),
                TimeSpan.FromMinutes(194), 0, []),
            new TransferLeg("G25", "240000G250L", Nanjing, Shanghai, new TimeOnly(20, 16), new TimeOnly(21, 18),
                TimeSpan.FromMinutes(62), 0, []),
            Nanjing, TimeSpan.FromMinutes(2), 2, TimeSpan.FromMinutes(258), 258, "4小时18分钟",
            new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), true, false);

        var h = new Harness();
        h.Transfer.Result = new TransferQueryResult([plan], 0, 0);
        h.Ready();

        await h.Vm.RunTransferAsync(default);

        Assert.Single(h.Vm.Transfers);
        Assert.StartsWith("中转_结果数", h.Vm.TransferStatus);
        Assert.Equal("17:00 出发 · 21:18 到达", ((TransferRowViewModel)h.Vm.Transfers[0]).TimesText);
    }

    [Fact]
    public async Task 有方案被丢弃时提示里必须说明()
    {
        var h = new Harness();
        h.Transfer.Result = new TransferQueryResult([], 2, 1);
        h.Ready();

        await h.Vm.RunTransferAsync(default);

        Assert.Contains("中转_已丢弃", h.Vm.TransferStatus);
    }

    // ── 收藏与历史（FR-16 / FR-17）──────────────────────────────

    [Fact]
    public void 收藏达到上限时给出明确提示而不是静默丢弃()
    {
        var h = new Harness();
        h.Book.Accepts = false;
        h.Ready();

        h.Vm.SaveCurrentRoute();

        Assert.Equal("收藏_已满", h.Vm.BookStatus);
    }

    [Fact]
    public void 删除收藏后其余顺序不变并说明删了哪条()
    {
        var h = new Harness();
        h.Book.Items.AddRange([
            new SavedRoute("甲", "AAA", "BBB"), new SavedRoute("乙", "CCC", "DDD"), new SavedRoute("丙", "EEE", "FFF")]);
        h.Vm.RefreshSaved();

        h.Vm.RemoveSaved("乙");

        Assert.Equal(["甲", "丙"], h.Vm.SavedRoutes.Select(r => r.Name));
        Assert.Equal("收藏_已删除", h.Vm.BookStatus);
    }

    [Fact]
    public void 点收藏只回填条件不自动查询()
    {
        // FR-16：日期必须由用户重新确认——收藏刻意不存日期。
        var h = new Harness();

        h.Vm.ApplySaved(new SavedRoute("北京南→上海虹桥", "VNP", "AOH"));

        Assert.Equal("北京南", h.Vm.FromText);
        Assert.Equal("上海虹桥", h.Vm.ToText);
        Assert.Equal(0, h.Query.Calls);
    }

    [Fact]
    public void 收藏里的站已不在码表时说清原因而不是回填一个错站名()
    {
        var h = new Harness();

        h.Vm.ApplySaved(new SavedRoute("老线", "ZZZ", "YYY"));

        Assert.Equal("收藏_站已不在", h.Vm.BookStatus);
        Assert.Equal(0, h.Query.Calls);
    }

    // ── 检查更新（FR-36）───────────────────────────────────────

    [Fact]
    public async Task 取不到版本时界面上不出现任何提示()
    {
        var h = new Harness();

        await h.Vm.CheckForUpdateAsync(default);

        Assert.False(h.Vm.HasUpdateNote);
        Assert.Equal(string.Empty, h.Vm.UpdateNote);
    }

    [Fact]
    public async Task 有更新时的提示同时给出版本号和当前版本()
    {
        var h = new Harness();
        h.Release.Latest = "9.9.9";
        h.Text.Map = key => key == "更新_有新版本" ? "发现新版本 {0}（当前 {1}）" : key;

        await h.Vm.CheckForUpdateAsync(default);

        Assert.True(h.Vm.HasUpdateNote);
        Assert.Contains("9.9.9", h.Vm.UpdateNote);
        Assert.Contains(h.Vm.AppVersion, h.Vm.UpdateNote);
    }

    [Fact]
    public void 开着开关时启动检查只发一次()
    {
        // FR-36 的时机约束："仅启动时一次，不定时、不轮询、不因用户操作重复发起"。
        // 替身是同步完成的，所以构造返回时那一次已经跑完——次数可以精确断言。
        var h = new Harness(new AppSettings(CheckUpdatesEnabled: true));

        Assert.Equal(1, h.Release.Calls);

        var off = new Harness(new AppSettings(CheckUpdatesEnabled: false));
        Assert.Equal(0, off.Release.Calls);
    }

    // ── 经停钻取（FR-14）───────────────────────────────────────

    [Fact]
    public async Task 选中一趟车后能取到经停站并标出上下车站()
    {
        var h = new Harness();
        h.Query.Result = [Journey("G1")];
        h.Ready();
        await h.Vm.QueryAsync(default);
        h.Vm.Selected = h.Vm.Rows[0];

        await h.Vm.LoadStopsAsync(default);

        Assert.Equal(3, h.Vm.Stops.Count);
        Assert.True(h.Vm.HasStops);
        Assert.Equal(1, h.Stops.Calls);
    }
}
