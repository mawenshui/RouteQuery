using System.Collections.ObjectModel;
using RouteQuery.Core.Errors;
using RouteQuery.Core.Logic;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>结果区的五种视图状态。刻意把"无结果"与"出错"分开（FR-10）。</summary>
public enum ViewState { Empty, Loading, Results, FilteredEmpty, Error, Guide }

/// <summary>主窗口视图模型。对应 FR-01 ~ FR-12、FR-14、FR-26 ~ FR-31。</summary>
public sealed class MainViewModel : Mvvm.ViewModelBase
{
    private readonly IStationRepository _stations;
    private readonly ITrainQueryService _query;
    private readonly ITrainStopService _stops;
    private readonly IExtensionQueryService _extension;
    private readonly ISettingsStore _settings;
    private readonly IQueryBudget _budget;
    private readonly ITextProvider _text;
    private readonly IOfficialLinkProvider _links;
    private readonly IOfficialSession _session;
    private readonly IRouteBook _book;
    private readonly IQueryHistory _history;

    private Station? _from;
    private Station? _to;
    private Station? _fromPick;
    private Station? _toPick;
    private string _fromText = string.Empty;
    private string _toText = string.Empty;
    private DateOnly _travelDate = DateOnly.FromDateTime(DateTime.Today);
    private ViewState _state = ViewState.Empty;
    private string _statusText;
    private string? _errorText;
    private bool _canRetry;
    private JourneyRowViewModel? _selected;
    private QueryCriteria _criteria = new();
    private SortKey _sortKey = SortKey.DepartureEarliest;
    private int _maxStations = 3;
    private string _extensionStatus = string.Empty;
    private int _dailyRemaining;

    public MainViewModel(
        IStationRepository stations,
        ITrainQueryService query,
        ITrainStopService stops,
        IExtensionQueryService extension,
        ISettingsStore settings,
        IQueryBudget budget,
        ITextProvider text,
        IOfficialLinkProvider links,
        IOfficialSession session,
        IRouteBook book,
        IQueryHistory history)
    {
        _stations = stations;
        _query = query;
        _stops = stops;
        _extension = extension;
        _settings = settings;
        _budget = budget;
        _text = text;
        _links = links;
        _session = session;
        _book = book;
        _history = history;
        _statusText = text.Get("状态_空");
        _dailyRemaining = _budget.DailyRemaining;

        MaxExtensionStations = _settings.Load().MaxExtensionStations;

        // 下拉默认选"全部"，避免进界面就带着一个隐含筛选却看不出来。
        _selectedKind = KindOptions[0];
        _selectedBand = BandOptions[0];
        _selectedSort = SortOptions[0];

        foreach (var r in _book.All) SavedRoutes.Add(r);
        foreach (var h in _history.All) History.Add(new HistoryRowViewModel(h, _stations, _text));

        QueryCommand = new Mvvm.AsyncCommand(QueryAsync);
        LoadStopsCommand = new Mvvm.AsyncCommand(LoadStopsAsync);
        RunExtensionCommand = new Mvvm.AsyncCommand(RunExtensionAsync);
        OpenOfficialCommand = new Mvvm.AsyncCommand(OpenOfficialAsync);
    }

    public Mvvm.AsyncCommand QueryCommand { get; }
    public Mvvm.AsyncCommand LoadStopsCommand { get; }
    public Mvvm.AsyncCommand RunExtensionCommand { get; }
    public Mvvm.AsyncCommand OpenOfficialCommand { get; }

    /// <summary>
    /// 用系统默认浏览器打开官方查询页（FR-19）。<b>本应用不发这个请求</b>——交给浏览器，
    /// 所以既没有额外的官方流量，也不碰官方的任何表单。
    /// <para>不带 OD 与日期参数：官方查询页的可带参数形态还没核实（Q-07），
    /// 拼一个猜出来的地址等于给亲友一个可能打不开的链接。改为在界面上提示手动输入。</para>
    /// </summary>
    private Task OpenOfficialAsync(CancellationToken ct)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _links.LeftTicketPageUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            DetailHint = _text.Get("跳转_失败");
            Raise(nameof(DetailHint));
        }

        return Task.CompletedTask;
    }

    // ── 收藏与历史（FR-16 / FR-17）──────────────────────────
    public ObservableCollection<SavedRoute> SavedRoutes { get; } = [];
    public ObservableCollection<HistoryRowViewModel> History { get; } = [];

    /// <summary>收藏/回填的反馈行。它和扩展面板的进度行分开，避免互相盖掉对方的话。</summary>
    public string BookStatus
    {
        get => _bookStatus;
        private set { if (Set(ref _bookStatus, value)) Raise(nameof(BookStatus)); }
    }

    private string _bookStatus = string.Empty;

    /// <summary>把当前条件存成收藏。名字用"出发→到达"，够认就行，不做重名编辑。</summary>
    public void SaveCurrentRoute()
    {
        if (_from is not { } f || _to is not { } t)
        {
            BookStatus = _text.Get("错误_输入无效");
            return;
        }

        BookStatus = _book.Add(new SavedRoute($"{f.Name}→{t.Name}", f.Telecode, t.Telecode))
            ? string.Format(_text.Get("收藏_已存"), f.Name, t.Name)
            : _text.Get("收藏_已满");
        RefreshSaved();
    }

    public void RemoveSaved(string name)
    {
        _book.Remove(name);
        RefreshSaved();
    }

    /// <summary>点收藏只回填条件，<b>不自动查询</b>——日期必须由用户重新确认（FR-16）。</summary>
    public void ApplySaved(SavedRoute route)
    {
        if (_stations.FindByTelecode(route.FromTelecode) is not { } f ||
            _stations.FindByTelecode(route.ToTelecode) is not { } t)
        {
            // 三字码在码表里找不到，多半是码表换版了。这时宁可说明，也不回填一个错的站名。
            BookStatus = _text.Get("收藏_站已不在");
            return;
        }

        PickFrom(f);
        PickTo(t);
        BookStatus = _text.Get("收藏_已回填");
    }

    public void ApplyHistory(HistoryRowViewModel row)
    {
        if (_stations.FindByTelecode(row.FromTelecode) is not { } f ||
            _stations.FindByTelecode(row.ToTelecode) is not { } t)
        {
            BookStatus = _text.Get("收藏_站已不在");
            return;
        }

        PickFrom(f);
        PickTo(t);
        TravelDate = row.TravelDate;
        BookStatus = _text.Get("历史_已回填");
    }

    public void ClearHistory()
    {
        _history.Clear();
        History.Clear();
        BookStatus = _text.Get("历史_已清空");
    }

    private void RefreshSaved()
    {
        SavedRoutes.Clear();
        foreach (var r in _book.All) SavedRoutes.Add(r);
    }

    // ── 登录态（FR-24 / FR-25）──────────────────────────────
    /// <summary>是否已持有官方登录态。界面用它决定中转页签显示引导还是显示"已登录 + 退出"。</summary>
    public bool HasLoginSession => _session.HasValidSession;

    /// <summary>登录状态那句话。未登录时<b>必须</b>同时说清"不登录能做什么"，
    /// 否则亲友会以为整个工具用不了（DEC-08：引导态不是错误态）。</summary>
    public string SessionSummary => HasLoginSession
        ? string.Format(_text.Get("登录_状态已登录"),
            _session.SavedAt?.ToString("M月d日 HH:mm") ?? _text.Get("提示_官方未给出"))
        : _text.Get("登录_状态未登录");

    /// <summary>登录页地址。字符串由数据层给出，界面只负责交给 WebView2（AGENTS 第六节）。</summary>
    public string LoginPageUrl => _links.LoginPageUrl;

    /// <summary>会话端口本身。登录窗口需要写它，退出按钮需要清它——都是 Core 接口，
    /// 界面拿到的是一个只有"写与清"的对象，拿不到 Cookie。</summary>
    public IOfficialSession Session => _session;

    /// <summary>登录窗口关掉之后由界面调用，让状态行重新取值。</summary>
    public void NotifySessionChanged() => Raise(nameof(HasLoginSession), nameof(SessionSummary));

    /// <summary>退出并清除：应用侧副本 + WebView 容器两处都要清（SPEC-007 三.4）。</summary>
    public void ClearSession()
    {
        _session.Clear();
        Web.WebProfileCleaner.Clear();
        NotifySessionChanged();
        ExtensionStatus = _text.Get("登录_已清除");
    }

    /// <summary>跳转后需要用户自己填的条件——原样复述一遍，省得他记两遍站名（FR-19 的"带不上参数"补偿）。</summary>
    public string OfficialReminder =>
        Selected is { } row
            ? string.Format(_text.Get("跳转_提示"), row.FromName, row.ToName, TravelDate.ToString("yyyy-MM-dd"))
            : _text.Get("跳转_未选");

    /// <summary>取消在途查询。切换条件时旧请求也必须被取消，其迟到结果不得覆盖新结果（FR-08）。</summary>
    public void CancelQuery() => QueryCommand.Cancel();

    // ── 输入区 ───────────────────────────────────────────────
    public string FromText
    {
        get => _fromText;
        set
        {
            if (Set(ref _fromText, value))
            {
                _from = null;
                _fromPick = null;
                Raise(nameof(FromPick));
                Populate(FromCandidates, value, _stations);
            }
        }
    }

    public string ToText
    {
        get => _toText;
        set
        {
            if (Set(ref _toText, value))
            {
                _to = null;
                _toPick = null;
                Raise(nameof(ToPick));
                Populate(ToCandidates, value, _stations);
            }
        }
    }

    public ObservableCollection<Station> FromCandidates { get; } = [];
    public ObservableCollection<Station> ToCandidates { get; } = [];

    public DateOnly TravelDate
    {
        get => _travelDate;
        set
        {
            // 范围内可点、范围外置灰，程序层再拒一次：不依赖控件行为（FR-03 验收②）。
            var window = PresaleWindow.For(_today);
            var clamped = value < window.Min ? window.Min : value > window.Max ? window.Max : value;
            if (Set(ref _travelDate, clamped)) Raise(nameof(TravelDateValue), nameof(DateMin), nameof(DateMax));
        }
    }

    /// <summary>DatePicker 的 SelectedDate 是 <c>DateTime?</c>，直接绑 DateOnly 会静默失效，所以显式转一次。
    /// 界面只认这一个日期口径，快捷按钮也走同一个 setter。</summary>
    public DateTime? TravelDateValue
    {
        get => _travelDate.ToDateTime(TimeOnly.MinValue);
        set { if (value is { } d) TravelDate = DateOnly.FromDateTime(d); }
    }

    /// <summary>今天 / 明天 / 后天三个快捷按钮（界面说明 FR-03 节）。越界由 TravelDate 自己钳回来。</summary>
    public void QuickDate(int offsetDays) => TravelDate = _today.AddDays(offsetDays);

    public DateTime DateMin => PresaleWindow.For(_today).Min.ToDateTime(TimeOnly.MinValue);
    public DateTime DateMax => PresaleWindow.For(_today).Max.ToDateTime(TimeOnly.MinValue);

    private static readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Today);

    public void PickFrom(Station s)
    {
        _from = s;
        _fromText = s.Name;
        Raise(nameof(FromText));
        FromCandidates.Clear();
    }

    public void PickTo(Station s)
    {
        _to = s;
        _toText = s.Name;
        Raise(nameof(ToText));
        ToCandidates.Clear();
    }

    /// <summary>
    /// 候选列表的选中项。<b>鼠标点选与键盘 ↓↑ 都只改这个属性</b>，提交动作集中在 setter 里——
    /// 之前用 MouseUp 事件提交，键盘路径与鼠标路径就成了两套，任何一套漏掉都会出现
    /// "看着选中了，点查询却说没选站"。
    /// </summary>
    public Station? FromPick
    {
        get => _fromPick;
        set { if (Set(ref _fromPick, value) && value is { } s) PickFrom(s); }
    }

    public Station? ToPick
    {
        get => _toPick;
        set { if (Set(ref _toPick, value) && value is { } s) PickTo(s); }
    }

    /// <summary>互换出发与到达。日期与筛选条件保持不变，结果区清空而不弹错误（FR-02）。</summary>
    public void Swap()
    {
        (_from, _to) = (_to, _from);
        (_fromText, _toText) = (_toText, _fromText);
        State = ViewState.Empty;
        Raise(nameof(FromText), nameof(ToText));
        FromCandidates.Clear();
        ToCandidates.Clear();
    }

    /// <summary>供界面在"输入框失焦"时调用：关掉候选弹层但不影响已选中的站。</summary>
    public void CloseCandidates()
    {
        FromCandidates.Clear();
        ToCandidates.Clear();
    }

    private void RaiseCandidates()
    {
        Populate(FromCandidates, FromText, _stations);
        Populate(ToCandidates, ToText, _stations);
    }

    private static void Populate(ObservableCollection<Station> target, string keyword, IStationRepository repository)
    {
        var hits = repository.Search(keyword);
        target.Clear();
        foreach (var h in hits) target.Add(h);
    }

    // ── 结果与状态 ───────────────────────────────────────────
    public ObservableCollection<JourneyRowViewModel> Rows { get; } = [];
    public ObservableCollection<StopRowViewModel> Stops { get; } = [];
    public ObservableCollection<ExtensionRowViewModel> Extensions { get; } = [];

    public IReadOnlyList<TrainJourney> Raw { get; private set; } = [];

    public ViewState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(IsLoading), nameof(HasResults), nameof(IsError), nameof(IsGuide),
                    nameof(IsEmptyOrFiltered), nameof(StatusText), nameof(ErrorText));
            }
        }
    }

    public bool IsLoading => State == ViewState.Loading;
    public bool HasResults => State == ViewState.Results;
    public bool IsError => State == ViewState.Error;
    public bool IsGuide => State == ViewState.Guide;
    public bool IsEmptyOrFiltered => State is ViewState.Empty or ViewState.FilteredEmpty;

    public string StatusText
    {
        get => _statusText;
        private set { if (Set(ref _statusText, value)) Raise(nameof(StatusText)); }
    }

    public string? ErrorText
    {
        get => _errorText;
        private set { if (Set(ref _errorText, value)) Raise(nameof(ErrorText)); }
    }

    /// <summary>只有网络类与超时才给重试按钮；风控与格式变更给了也没用（SPEC-007 三.7）。</summary>
    public bool CanRetry { get => _canRetry; private set { if (Set(ref _canRetry, value)) Raise(nameof(CanRetry)); } }

    public JourneyRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Stops.Clear();
                Extensions.Clear();
                ExtensionStatus = string.Empty;
                DetailHint = _text.Get("详情_空提示");
                Raise(nameof(HasSelection), nameof(HasStops), nameof(DetailHint), nameof(OfficialReminder));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>经停表是否有内容。空表会被读成"这趟车只停两站"，所以空时必须换成一句说明（FR-14 边界）。</summary>
    public bool HasStops => Stops.Count > 0;

    public string DetailHint { get; private set; } = string.Empty;

    public int DailyRemaining
    {
        get => _dailyRemaining;
        private set { if (Set(ref _dailyRemaining, value)) Raise(nameof(DailyRemaining), nameof(DailyRemainingText)); }
    }

    /// <summary>额度那句话整句来自文案表，不在 XAML 里拼字符串（SPEC-004 五.5）。</summary>
    public string DailyRemainingText => string.Format(_text.Get("状态_剩余额度"), DailyRemaining);

    // ── 筛选与排序（FR-11 / FR-12）───────────────────────────
    public QueryCriteria Criteria
    {
        get => _criteria;
        set { _criteria = value; ApplyLocalView(); }
    }

    public SortKey SortKey
    {
        get => _sortKey;
        set { _sortKey = value; ApplyLocalView(); }
    }

    public void ClearFilters() => Criteria = new QueryCriteria();

    // ── 下拉选项（界面绑定这些，不在 code-behind 里拼控件）────────
    /// <summary>带值与中文标签的选项。</summary>
    public sealed record Option<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<Option<TrainKind?>> KindOptions { get; } =
        [
            new(null, "全部车型"),
            new(TrainKind.GaoTie, "高铁 G"),
            new(TrainKind.DongChe, "动车 D"),
            new(TrainKind.ChengJi, "城际 C"),
            new(TrainKind.ZhiDa, "直达特快 Z"),
            new(TrainKind.TeKuai, "特快 T"),
            new(TrainKind.KuaiSu, "快速 K"),
            new(TrainKind.PuKe, "普客"),
        ];

    public IReadOnlyList<Option<DepartureBand?>> BandOptions { get; } =
        [
            new(null, "全部时段"),
            new(DepartureBand.Morning, "上午 06–12"),
            new(DepartureBand.Afternoon, "下午 12–18"),
            new(DepartureBand.Evening, "晚上 18–24"),
            new(DepartureBand.Night, "凌晨 00–06"),
        ];

    public IReadOnlyList<Option<SortKey>> SortOptions { get; } =
        [
            new(SortKey.DepartureEarliest, "最早出发"),
            new(SortKey.DepartureLatest, "最晚出发"),
            new(SortKey.ShortestDuration, "历时最短"),
            new(SortKey.ArrivalEarliest, "到达最早"),
            new(SortKey.LowestPrice, "最低价"),
        ];

    public IReadOnlyList<int> StationChoices { get; } = Enumerable.Range(1, 10).ToList();

    private Option<TrainKind?> _selectedKind = null!;
    private Option<DepartureBand?> _selectedBand = null!;
    private Option<SortKey> _selectedSort = null!;
    private bool _onlyAvailable;
    private bool _sameDayArrival;

    public Option<TrainKind?> SelectedKind
    {
        get => _selectedKind;
        set { if (Set(ref _selectedKind, value)) RebuildCriteria(); }
    }

    public Option<DepartureBand?> SelectedBand
    {
        get => _selectedBand;
        set { if (Set(ref _selectedBand, value)) RebuildCriteria(); }
    }

    public Option<SortKey> SelectedSort
    {
        get => _selectedSort;
        set { if (Set(ref _selectedSort, value)) { SortKey = value.Value; } }
    }

    public bool OnlyAvailable
    {
        get => _onlyAvailable;
        set { if (Set(ref _onlyAvailable, value)) RebuildCriteria(); }
    }

    public bool SameDayArrival
    {
        get => _sameDayArrival;
        set { if (Set(ref _sameDayArrival, value)) RebuildCriteria(); }
    }

    private void RebuildCriteria()
    {
        Criteria = new QueryCriteria
        {
            Kinds = SelectedKind.Value is { } k ? [k] : [],
            Bands = SelectedBand.Value is { } b ? [b] : [],
            OnlyAvailable = OnlyAvailable,
            SameDayArrival = SameDayArrival,
        };
    }

    /// <summary>查询按钮文案。进行中改文案并禁用，避免用户重复触发（FR-08）。</summary>
    public string QueryButtonText => IsLoading ? _text.Get("按钮_查询中") : _text.Get("按钮_查询");

    /// <summary>状态栏的站点数据日期，让亲友能自查是不是码表过期（FR-18）。</summary>
    public string StationDataDate => _stations.IsLoaded ? _stations.SourceDate.ToString("yyyy-MM-dd") : "未载入";

    public Mvvm.AsyncCommand ClearFiltersCommand => _clear ??= new Mvvm.AsyncCommand(_ => { ClearFilters(); return Task.CompletedTask; });
    private Mvvm.AsyncCommand? _clear;

    /// <summary>筛选与排序全在内存，不产生任何网络请求（FR-11 验收③）。</summary>
    private void ApplyLocalView()
    {
        if (State is ViewState.Empty or ViewState.Error or ViewState.Guide or ViewState.Loading) return;

        var filtered = JourneyFilter.Apply(Raw, Criteria);
        var sorted = JourneySorter.Apply(filtered, SortKey);

        Rows.Clear();
        foreach (var j in sorted) Rows.Add(new JourneyRowViewModel(j, _text));

        State = sorted.Count > 0 ? ViewState.Results
            : Raw.Count == 0 ? ViewState.Results
            : ViewState.FilteredEmpty;

        StatusText = State == ViewState.Results
            ? string.Format(_text.Get("状态_结果数"), sorted.Count)
            : _text.Get("状态_筛选后空");
    }

    // ── 查询 ─────────────────────────────────────────────────
    private async Task QueryAsync(CancellationToken ct)
    {
        if (!TryValidate(out var invalidMessage))
        {
            ShowError(invalidMessage, retry: false);
            return;
        }

        State = ViewState.Loading;
        StatusText = _text.Get("状态_加载");
        ErrorText = null;
        Rows.Clear();
        Selected = null;

        try
        {
            var request = new DirectQueryRequest(_from!, _to!, TravelDate);
            var journeys = await _query.SearchAsync(request, ct);

            Raw = journeys;
            DailyRemaining = _budget.DailyRemaining;

            // 出错时不记历史：一次失败的查询不该占掉一条位置，也不该被误当成"查过了"。
            // 存进文件与加进列表用同一个 entry 对象，避免两处时间戳不一致。
            var entry = new QueryHistoryEntry(request.From.Telecode, request.To.Telecode, request.TravelDate, journeys.Count, DateTimeOffset.Now);
            _history.Record(entry);
            History.Insert(0, new HistoryRowViewModel(entry, _stations, _text));
            while (History.Count > IQueryHistory.MaxEntries) History.RemoveAt(History.Count - 1);

            if (journeys.Count == 0)
            {
                State = ViewState.Results;
                Rows.Clear();
                StatusText = _text.Get("状态_无结果");
                return;
            }

            // 先落状态再刷视图：ApplyLocalView 对"还在加载"是直接跳过的，
            // 顺序反过来会让一次成功的查询永远停在转圈上（实测踩过）。
            State = ViewState.Results;
            ApplyLocalView();
        }
        catch (OperationCanceledException)
        {
            State = Rows.Count > 0 ? ViewState.Results : ViewState.Empty;
            StatusText = _text.Get("错误_已取消");
        }
        catch (RateLimitedException ex)
        {
            ShowError(RateLimitText(ex.Reason), retry: false);
        }
        catch (QueryException ex)
        {
            ShowError(QueryErrorText(ex.Kind), ex.AllowsUserRetry);
        }
    }

    /// <summary>本地校验全部在网络之前（FR-04：非法输入时官方请求发出次数必须为 0）。</summary>
    private bool TryValidate(out string message)
    {
        if (!_stations.IsLoaded)
        {
            message = _text.Get("错误_站点数据缺失");
            return false;
        }
        if (_from is null || _to is null)
        {
            message = _text.Get("错误_输入无效");
            return false;
        }
        if (string.Equals(_from.Telecode, _to.Telecode, StringComparison.Ordinal))
        {
            message = _text.Get("错误_输入无效");
            return false;
        }
        if (!PresaleWindow.IsSelectable(TravelDate, _today))
        {
            message = _text.Get("错误_日期越界");
            return false;
        }

        message = string.Empty;
        return true;
    }

    private void ShowError(string text, bool retry)
    {
        State = ViewState.Error;
        ErrorText = text;
        StatusText = text;
        CanRetry = retry;
    }

    private string QueryErrorText(QueryErrorKind kind) => kind switch
    {
        QueryErrorKind.NetworkUnavailable => _text.Get("错误_网络不可用"),
        QueryErrorKind.Timeout => _text.Get("错误_超时"),
        QueryErrorKind.UpstreamRejected => _text.Get("错误_被拒绝"),
        QueryErrorKind.SessionRequired => _text.Get("中转_未登录"),
        QueryErrorKind.ParseFailure => _text.Get("错误_格式变更"),
        QueryErrorKind.DataUnavailable => _text.Get("错误_数据缺失"),
        _ => _text.Get("错误_被拒绝"),
    };

    private string RateLimitText(RateLimitReason reason) => reason switch
    {
        RateLimitReason.TooSoon => _text.Get("错误_太频繁"),
        RateLimitReason.AlreadyRunning => _text.Get("错误_正在查询"),
        RateLimitReason.DailyBudgetExceeded => _text.Get("错误_今日太多"),
        _ => _text.Get("错误_太频繁"),
    };

    // ── 经停站（FR-14）──────────────────────────────────────
    private async Task LoadStopsAsync(CancellationToken ct)
    {
        // 先固定住这一行：await 期间用户可能点了别的车次，迟到结果不能写到新的选中行上（FR-08）。
        if (Selected is not { } row) return;

        try
        {
            var route = await _stops.GetRouteAsync(
                row.Model.TrainNo, row.Model.From.Telecode, row.Model.To.Telecode, TravelDate, ct);

            if (!ReferenceEquals(Selected, row)) return;

            Stops.Clear();
            foreach (var s in route.Stops)
                Stops.Add(new StopRowViewModel(s, row.Model.From.Name, row.Model.To.Name, _text));

            DetailHint = string.Empty;
        }
        catch (QueryException ex)
        {
            // 明确说"官方没给"或"出错了"，而不是显示一张空表让人以为这趟车只停两站（NFR-18）。
            DetailHint = QueryErrorText(ex.Kind);
        }
        catch (RateLimitedException ex)
        {
            DetailHint = RateLimitText(ex.Reason);
        }
        catch (OperationCanceledException)
        {
            DetailHint = _text.Get("错误_已取消");
        }
        finally
        {
            Raise(nameof(HasStops), nameof(DetailHint));
        }
    }

    // ── 多买几站（FR-26 ~ FR-29）────────────────────────────
    public int MaxExtensionStations
    {
        get => _maxStations;
        set
        {
            var clamped = Math.Clamp(value, AppSettings.MinStations, AppSettings.MaxStations);
            if (Set(ref _maxStations, clamped))
            {
                // 在现有设置上改这一项，不整体覆盖——否则会把"首次声明已确认"之类的标记一起抹掉，
                // 用户下次启动又会被弹一次确认框。
                _settings.Save(_settings.Load() with { MaxExtensionStations = clamped });
                Raise(nameof(ExtensionEtaSeconds), nameof(ExtensionSummary));
            }
        }
    }

    /// <summary>发起前把耗时摊给用户看，让他自己决定要不要开始（NFR-17③）。</summary>
    public int ExtensionEtaSeconds => Math.Min(MaxExtensionStations, 10) * 3;

    public string ExtensionSummary =>
        string.Format(_text.Get("扩展_预估"), ExtensionEtaSeconds, Math.Min(MaxExtensionStations, 10));

    /// <summary>口径说明一句话，让用户知道"多花的钱"怎么算出来的（FR-28 验收要求金额与站数同行可读）。</summary>
    public string ExtensionDisclaimer => _text.Get("扩展_说明");

    public string ExtensionStatus
    {
        get => _extensionStatus;
        private set { if (Set(ref _extensionStatus, value)) Raise(nameof(ExtensionStatus)); }
    }

    private async Task RunExtensionAsync(CancellationToken ct)
    {
        if (Selected is null) return;

        Extensions.Clear();
        ExtensionStatus = _text.Get("扩展_标题");

        var progress = new Progress<Core.Ports.ExtensionProgress>(p =>
        {
            Extensions.Add(new ExtensionRowViewModel(p.Outcome, Selected.Model, _text));
            ExtensionStatus = string.Format(_text.Get("扩展_进度"), p.Done, p.Total);
            DailyRemaining = _budget.DailyRemaining;
        });

        try
        {
            var batch = await _extension.RunAsync(
                Selected.Model, TravelDate, MaxExtensionStations, progress, ct);

            if (batch.StopReason == ExtensionStopReason.AbortedByUpstream)
                ExtensionStatus = _text.Get("扩展_中止");
            else if (batch.CandidatesNotQueried > 0)
                ExtensionStatus = string.Format(_text.Get("扩展_未查完"), batch.CandidatesNotQueried);
            else
                ExtensionStatus = string.Format(_text.Get("扩展_进度"), batch.Outcomes.Count, batch.Outcomes.Count);
        }
        catch (QueryException ex) when (ex.Kind == QueryErrorKind.DataUnavailable)
        {
            ExtensionStatus = _text.Get("错误_数据缺失");
        }
        catch (RateLimitedException ex)
        {
            ExtensionStatus = RateLimitText(ex.Reason);
        }
        catch (OperationCanceledException)
        {
            ExtensionStatus = _text.Get("错误_已取消");
        }
    }
}
