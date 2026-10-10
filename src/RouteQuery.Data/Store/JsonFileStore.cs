using System.Text.Json;

namespace RouteQuery.Data.Store;

/// <summary>
/// 本地 JSON 读写。<b>一律走"临时文件 + 原子替换"</b>：更新到一半断电的最坏结果是回到旧文件，
/// 而不是留下半个坏文件把应用变成不可用（FR-18 验收②的实现手段）。
/// </summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            // 文件损坏时不崩，也不静默"重置成默认值"——那会让用户以为是自己改坏了设置。
            return null;
        }
    }

    public static void Write<T>(string path, T value) => WriteAtomic(path, JsonSerializer.Serialize(value, Pretty));
}
