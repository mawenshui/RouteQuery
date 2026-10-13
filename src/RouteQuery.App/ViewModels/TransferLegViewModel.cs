using System.Collections.ObjectModel;
using RouteQuery.Core.Model;
using RouteQuery.Core.Ports;

namespace RouteQuery.App.ViewModels;

/// <summary>
/// 中转方案里的一段，带<b>自己的</b>经停展开状态。对应 FR-15"两段各自可展开、可折叠，互不干扰"。
/// <para>为什么不共用那块详情面板：共用就只能同时看到一段，而"互不干扰"是验收原话。
/// 比方案的人要的是把两段的经停并排对着读。</para>
/// <para>展开用的四个参数全部来自官方该段自己的字段（<see cref="TransferLeg.TravelDate"/> 的注释里
/// 写了为什么不能拿整程日期顶替）。取不到时这里是<b>一句错误</b>，不是一张空表（NFR-18）。</para>
/// </summary>
public sealed class TransferLegViewModel : Mvvm.ViewModelBase
{
    private bool _isExpanded;
    private string? _errorText;

    public TransferLegViewModel(TransferLeg leg, string label, ITextProvider text)
    {
        Model = leg;
        Label = label;
        Text = text;
        SeatsText = SeatTexts.Summary(leg.Seats, text);
        // 这段是哪天、哪趟，展开后必须留在屏幕上：日期是官方段内字段而不是用户选的那天，
        // 不写出来的话用户无法判断这张时刻表是从哪天取的。
        HeadText = $"{leg.TrainCode} · {leg.From.Name}→{leg.To.Name} · {leg.TravelDate:MM-dd}开";
    }

    private ITextProvider Text { get; }

    public TransferLeg Model { get; }

    /// <summary>"段一" / "段二"。</summary>
    public string Label { get; }

    public string SeatsText { get; }

    public string HeadText { get; }

    public ObservableCollection<StopRowViewModel> Stops { get; } = [];

    public bool HasStops => Stops.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (Set(ref _isExpanded, value)) Raise(nameof(IsExpanded), nameof(ToggleLabel));
        }
    }

    /// <summary>按钮上的话。展开过之后再点就是收起，不能还写着"看经停"。</summary>
    public string ToggleLabel => IsExpanded ? Text.Get("按钮_收起经停") : Text.Get("按钮_看经停");

    /// <summary>这一次展开失败时给的一句话；成功就清空。</summary>
    public string? ErrorText
    {
        get => _errorText;
        private set { if (Set(ref _errorText, value)) Raise(nameof(ErrorText), nameof(HasError)); }
    }

    public bool HasError => ErrorText is not null;

    /// <summary>正在取数时按钮自己会禁用（<c>AsyncCommand</c> 的既为行为），这里不再另加一行提示。</summary>

    /// <summary>展开或收起这一段。由 <see cref="MainViewModel"/> 在建行时接上，因为取数要经它手里的经停服务。</summary>
    public Mvvm.AsyncCommand ToggleCommand { get; private set; } = null!;

    internal void AttachToggle(Mvvm.AsyncCommand command) => ToggleCommand = command;

    /// <summary>取到了：填进这一段自己的列表并展开。参数是官方该段的 OD，高亮按段算而不是按整程算。</summary>
    internal void Show(IReadOnlyList<StopDetail> stops)
    {
        Stops.Clear();
        foreach (var s in stops)
            Stops.Add(new StopRowViewModel(s, Model.From.Name, Model.To.Name, Text));

        ErrorText = null;
        IsExpanded = true;
        Raise(nameof(HasStops), nameof(ToggleLabel));
    }

    /// <summary>收起过再点开：直接用已经在手里的那份，不再花一次官方额度（FR-07）。</summary>
    internal void Reveal()
    {
        ErrorText = null;
        IsExpanded = true;
        Raise(nameof(ToggleLabel));
    }

    /// <summary>取不到：只说一句话，<b>不清掉另一段，也不留一张空表</b>（NFR-18、FR-14 的收紧口径）。</summary>
    internal void Fail(string message)
    {
        ErrorText = message;
        IsExpanded = false;
        Raise(nameof(HasStops), nameof(ToggleLabel));
    }

    internal void Collapse()
    {
        IsExpanded = false;
        Raise(nameof(ToggleLabel));
    }
}
