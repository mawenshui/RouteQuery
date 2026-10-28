using System.IO;
using RouteQuery.App;

namespace RouteQuery.Tests;

/// <summary>
/// 站点候选弹层的键盘选择路径（用例 TC-A4）。
/// <para>拆成两半守。下标算术直接断言 <see cref="MainWindow.NextIndex"/>；"回车提交走哪条路"用读源文件的方式守——
/// 因为 WPF 的绑定写错属性名不抛异常，只是选不中，而"选不中"在屏幕上跟"按键没送达"长得一模一样。
/// 这条用例此前只能靠真键盘，实测时恰恰撞上了"按了两次只生效一次"这种分不清是谁的问题。</para>
/// </summary>
public class PickerKeyTests
{
    [Theory]
    [InlineData(-1, 0)]   // 弹层刚打开时没有预选：第一次 ↓ 高亮第 1 条
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(10, 11)]
    public void 每按一次向下只下移一条(int before, int after) =>
        Assert.Equal(after, MainWindow.NextIndex(before, 12, +1));

    [Fact]
    public void 第n次向下高亮第n条()
    {
        const int count = 12;
        var index = -1;                                   // 弹层打开时的真实初值
        for (var press = 1; press <= count; press++)
        {
            index = MainWindow.NextIndex(index, count, +1);
            Assert.Equal(press - 1, index);               // 第 1 次 → 下标 0 → 界面上的第 1 条
        }
    }

    [Fact]
    public void 到底再向下回到第一条且到顶再向上回到最后一条()
    {
        const int count = 12;
        Assert.Equal(0, MainWindow.NextIndex(count - 1, count, +1));
        Assert.Equal(count - 1, MainWindow.NextIndex(0, count, -1));
    }

    [Fact]
    public void 输入框与候选列表的接线还在原处()
    {
        var dir = Path.Combine(Samples.RepoRoot, "src", "RouteQuery.App");
        var xaml = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml"));
        var cs = File.ReadAllText(Path.Combine(dir, "MainWindow.xaml.cs"));

        Assert.Contains("PreviewKeyDown=\"FromBox_PreviewKeyDown\"", xaml);
        Assert.Contains("PreviewKeyDown=\"ToBox_PreviewKeyDown\"", xaml);
        // 提交只有一条路径：列表选中项 → 视图模型属性（DEC-17）。少了 Mode=TwoWay 就变成"看着选中了但没提交"。
        Assert.Contains("SelectedItem=\"{Binding FromPick, Mode=TwoWay}\"", xaml);
        Assert.Contains("SelectedItem=\"{Binding ToPick, Mode=TwoWay}\"", xaml);
        Assert.Contains("candidates.Clear();", cs);       // 回车与 Esc 收起弹层
    }
}
