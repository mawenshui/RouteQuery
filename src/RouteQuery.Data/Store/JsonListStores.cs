using System.Text.Json;
using RouteQuery.Core.Ports;

namespace RouteQuery.Data.Store;

/// <summary>
/// 收藏与历史的 JSON 存储。对应 FR-16 / FR-17，落在 <c>%LOCALAPPDATA%\RouteQuery\</c>。
/// <para>两者共用同一套读写：都是"整份小列表 + 原子替换"。列表规模上限只有 20 / 30 条，
/// 所以不做增量写、不做数据库——那才是过度设计。</para>
/// <para><b>读不出来时返回空列表而不是崩</b>：收藏丢了很难受，但因为收藏文件坏了就打不开应用，
/// 是更难受的那种错。</para>
/// </summary>
internal static class JsonList
{
    public static List<T> Read<T>(string path)
    {
        try
        {
            return JsonFileStore.Read<List<T>>(path) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Write<T>(string path, IReadOnlyList<T> items) =>
        JsonFileStore.WriteAtomic(path, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>收藏的本地存储。</summary>
public sealed class JsonRouteBook : IRouteBook
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly List<SavedRoute> _items;

    public IReadOnlyList<SavedRoute> All
    {
        get { lock (_sync) return _items.ToList(); }
    }

    /// <summary>构造时一次性读入，之后每次修改整份原子重写。</summary>
    public JsonRouteBook(string? path = null)
    {
        _path = path ?? AppPaths.SavedRoutes;
        _items = JsonList.Read<SavedRoute>(_path);
    }

    public bool Add(SavedRoute route)
    {
        lock (_sync)
        {
            if (_items.Count >= IRouteBook.MaxEntries) return false;
            if (_items.Any(r => r.Name == route.Name)) return false;   // 同名视为已存在，不静默覆盖

            _items.Add(route);
            JsonList.Write(_path, _items);
            return true;
        }
    }

    public bool Remove(string name)
    {
        lock (_sync)
        {
            var removed = _items.RemoveAll(r => r.Name == name) > 0;
            if (removed) JsonList.Write(_path, _items);
            return removed;
        }
    }
}

/// <summary>查询历史的本地存储。新记录插在开头，超过 30 条从尾部淘汰。</summary>
public sealed class JsonQueryHistory : IQueryHistory
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly List<QueryHistoryEntry> _items;

    public JsonQueryHistory(string? path = null)
    {
        _path = path ?? AppPaths.History;
        _items = JsonList.Read<QueryHistoryEntry>(_path);
    }

    public IReadOnlyList<QueryHistoryEntry> All
    {
        get { lock (_sync) return _items.ToList(); }
    }

    public void Record(QueryHistoryEntry entry)
    {
        lock (_sync)
        {
            // 同一 OD + 同一日期不重复堆叠：连查三次同一程不该占掉三条历史。
            _items.RemoveAll(e => e.FromTelecode == entry.FromTelecode
                                  && e.ToTelecode == entry.ToTelecode
                                  && e.TravelDate == entry.TravelDate);
            _items.Insert(0, entry);
            if (_items.Count > IQueryHistory.MaxEntries) _items.RemoveRange(IQueryHistory.MaxEntries, _items.Count - IQueryHistory.MaxEntries);
            JsonList.Write(_path, _items);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            JsonList.Write(_path, _items);
        }
    }
}
