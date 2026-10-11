namespace RouteQuery.App.Update;

/// <summary>
/// 版本号比较（FR-36）。
/// <para>只认开头的数字三段，后缀单独判一次：<c>v0.3.0</c>、<c>0.3</c>、<c>0.3.0-rc1</c> 都归一成
/// <c>(0,3,0)</c>。三段相等时，只有"本地带后缀而线上不带"才算升级（rc → 正式版是真升级）；
/// 两边都带后缀一律<b>不提示</b>。</para>
/// <para>为什么不直接比字符串：<c>"0.2.0" &lt; "0.2.0-rc2"</c> 在序上成立，于是正式版会被判成比 rc 旧，
/// 用户会反复看到一个"有新版本"其实是回退。为什么 rc1 → rc2 不提示：后缀没有可比较的通用规则，
/// 猜出来的顺序只会让用户在状态栏看到一个说不清的提示。弱提示宁可不出现，也不要出现错的那一句。</para>
/// </summary>
public static class ReleaseChecker
{
    public static bool IsNewer(string? latest, string current)
    {
        var remote = Parse(latest);
        var local = Parse(current);
        if (remote is null || local is null) return false;

        var (remoteNumber, remoteIsFinal) = remote.Value;
        var (localNumber, localIsFinal) = local.Value;

        var order = remoteNumber.CompareTo(localNumber);
        return order > 0 || (order == 0 && remoteIsFinal && !localIsFinal);
    }

    /// <summary>解出开头的数字三段，并给出"这是不是不带后缀的正式版"。超出三段或带非数字的一律视为无效。</summary>
    private static (Version Number, bool IsFinal)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var body = text.Trim().TrimStart('v', 'V');
        var core = body.Split('-', '+', ' ')[0];
        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0)) return null;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out numbers[i])) return null;

        return (new Version(numbers[0], numbers[1], numbers[2]), body == core);
    }
}
