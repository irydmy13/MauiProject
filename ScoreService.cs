using Microsoft.Maui.Storage;
using System.Text.Json;

namespace MauiProject;

public class ScoreItem
{
    public string Name { get; set; } = "Player";
    public TimeSpan Time { get; set; } = TimeSpan.Zero;  // общее время прохождения
    public DateTime When { get; set; } = DateTime.Now;   // когда сыграли
}

public static class ScoreService
{
    private const string Key = "scores_time_v1";

    public static List<ScoreItem> GetScores()
    {
        var json = Preferences.Get(Key, "");
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<ScoreItem>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Add(ScoreItem item)
    {
        var list = GetScores();
        list.Add(item);
        // рейтинг по возрастанию времени (чем быстрее — тем выше)
        list = list.OrderBy(x => x.Time).ThenBy(x => x.When).ToList();
        Preferences.Set(Key, JsonSerializer.Serialize(list));
    }

    public static void Clear() => Preferences.Remove(Key);
}
