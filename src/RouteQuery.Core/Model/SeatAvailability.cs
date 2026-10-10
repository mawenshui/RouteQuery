namespace RouteQuery.Core.Model;

/// <summary>
/// 席别余票的状态。四态而非两态——这是实测换来的结论。
/// <para>官方页面会把"无票但可候补"渲染成<b>候补</b>，而 <c>*</c> 表示"尚未开售、官方未给该项数据"
/// （SPEC-007 v1.7 规则 5、7）。把这两者当成"有票"会让"只看有票"筛出一堆买不到的车次，
/// 当成"无票"则会误导用户放弃本可以候补的车次。</para>
/// </summary>
public enum SeatState
{
    /// <summary>有票：官方原始值为 <c>有</c> 或数字。</summary>
    Available,

    /// <summary>无票：官方原始值为 <c>无</c> 或空，且不满足候补条件。</summary>
    SoldOut,

    /// <summary>可候补：原始值恰为 <c>无</c>、该席别不是无座/其他、且车次候补标记为 1。</summary>
    Waitlist,

    /// <summary>该日期尚未开售，官方没有给出这个席别的数据。不得当作"无票"显示。</summary>
    NotYetOnSale,

    /// <summary>官方给了一个本项目无法解释的值（如出现新的符号）。原样透传，不参与任何判断。</summary>
    Unknown,
}

/// <summary>
/// 某一席别在某一查询区间上的余票与票价。对应 FR-09 / FR-13。
/// </summary>
/// <param name="Class">席别类别（余票列侧编码）。</param>
/// <param name="Raw">官方原始值，<b>原样保留</b>，界面优先展示它而不是本类型的推断结果（FR-13）。</param>
/// <param name="State">四态判定结果。</param>
/// <param name="Price">该席别票面价（来自票价侧短码解码）；缺失时为 null，此时界面显示"无法计算"而不是 0。</param>
public sealed record SeatAvailability(SeatClass Class, string Raw, SeatState State, decimal? Price)
{
    /// <summary>
    /// "只看有票"筛选的唯一判据。<see cref="SeatState.Waitlist"/> 与
    /// <see cref="SeatState.NotYetOnSale"/> 都<b>不</b>算有票。
    /// </summary>
    public bool IsAvailable => State == SeatState.Available;
}
