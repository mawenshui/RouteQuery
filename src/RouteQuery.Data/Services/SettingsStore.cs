using RouteQuery.Core.Ports;
using RouteQuery.Data.Http;
using RouteQuery.Data.Store;

namespace RouteQuery.Data.Services;

/// <summary>设置的 JSON 持久化。原子替换，避免写一半掉电留下坏文件（DEC-09）。</summary>
public sealed class SettingsStore(string? path = null) : ISettingsStore
{
    private readonly string _path = path ?? AppPaths.Settings;

    public AppSettings Load() => JsonFileStore.Read<AppSettings>(_path)?.Clamped() ?? new AppSettings();

    public void Save(AppSettings settings) => JsonFileStore.Write(_path, settings.Clamped());
}

/// <summary>把闸门的当日余量以只读形式暴露给界面（IQueryBudget 的实现）。</summary>
public sealed class QueryBudgetAdapter(RequestGate gate) : IQueryBudget
{
    public int DailyRemaining
    {
        get
        {
            var cap = gate.SignedIn ? 100 : 200;
            var used = gate.DailyCount;
            return Math.Max(0, cap - used);
        }
    }
}
