using System.Text.Json;

namespace ASPAssistant.Core.Data;

public sealed record UpdateHistoryEntry(string Version, DateOnly PublishedDate, string Title, string Content);

public static class UpdateHistory
{
    public static IReadOnlyList<UpdateHistoryEntry> Load(string path)
    {
        var entries = JsonSerializer.Deserialize<List<UpdateHistoryEntry>>(File.ReadAllText(path));
        if (entries is null || entries.Count == 0) throw new InvalidDataException("更新日志为空。");
        return entries.Select(entry =>
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Version) || !System.Version.TryParse(entry.Version.TrimStart('v'), out var version)
                || string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.Content))
                throw new InvalidDataException("更新日志包含无效的版本或内容。");
            return (Entry: entry, Version: version);
        }).OrderByDescending(item => item.Version).Select(item => item.Entry).ToArray();
    }
}
