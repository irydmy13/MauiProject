using Microsoft.Maui.Storage;
using System.Text.Json;

namespace MauiProject;

public class ScoreItem
{
    public string Name { get; set; } = "Player";
    public int Level { get; set; } = 0;              // <Ч Ќќ¬ќ≈: уровень, на котором завершили
    public TimeSpan Time { get; set; } = TimeSpan.Zero;
    public DateTime When { get; set; } = DateTime.Now;
}

public static class ScoreService
{
    private const string Key = "scores_time_v1";

    public static List<ScoreItem> GetScores()
    {
        var json = Preferences.Get(Key, "");
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<ScoreItem>>(json) ?? new(); }
        catch { return new(); }
    }

    public static void Add(ScoreItem item)
    {
        var list = GetScores();
        list.Add(item);
        // —начала выше тот, кто дошЄл дальше, при равном уровне Ч быстрее по времени
        list = list
            .OrderByDescending(x => x.Level)
            .ThenBy(x => x.Time)
            .ThenBy(x => x.When)
            .ToList();

        Preferences.Set(Key, JsonSerializer.Serialize(list));
    }

    public static void Clear() => Preferences.Remove(Key);
}
