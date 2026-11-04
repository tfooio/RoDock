using System.Text.Json;

namespace RoDock;

/// Hour-by-hour counts behind the dashboard charts: minutes each account spent in game, and how often a dropped account was relaunched.
/// Plain JSON in history.json (nothing secret); the last 8 days are kept.
public class History
{
    public class Bucket
    {
        public Dictionary<long, int> Minutes { get; set; } = [];
        public int Rejoins { get; set; }
    }

    readonly string _path;
    readonly Dictionary<long, Bucket> _hours = [];

    public History(string path)
    {
        _path = path;
        try { if (File.Exists(path)) _hours = JsonSerializer.Deserialize<Dictionary<long, Bucket>>(File.ReadAllText(path)) ?? []; }
        catch { } // a lost history is not worth an error
    }

    static long HourOf(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalHours);

    internal void Add(DateTime utc, long userId, int minutes, int rejoins)
    {
        var h = HourOf(utc);
        if (!_hours.TryGetValue(h, out var b)) _hours[h] = b = new();
        if (minutes > 0) b.Minutes[userId] = b.Minutes.GetValueOrDefault(userId) + minutes;
        b.Rejoins += rejoins;
    }

    public void InGameMinute(long userId) => Add(DateTime.UtcNow, userId, 1, 0);
    public void Rejoin() => Add(DateTime.UtcNow, 0, 0, 1);

    public void Save()
    {
        var cutoff = HourOf(DateTime.UtcNow) - 8 * 24;
        foreach (var k in _hours.Keys.Where(k => k < cutoff).ToList()) _hours.Remove(k);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(_hours)); }
        catch { }
    }

    /// Average number of accounts in game during each of the last `count` hours, oldest first (the current hour is averaged over the minutes so far).
    public double[] Hourly(int count)
    {
        var now = DateTime.UtcNow;
        var cur = HourOf(now);
        return Enumerable.Range(0, count).Select(i =>
        {
            var h = cur - (count - 1 - i);
            var minutes = _hours.TryGetValue(h, out var b) ? b.Minutes.Values.Sum() : 0;
            return minutes / (h == cur ? Math.Max(1.0, now.Minute + 1) : 60.0);
        }).ToArray();
    }

    /// Relaunches per local day for the last `days` days, oldest first.
    public (DateTime Day, int Rejoins)[] RejoinsPerDay(int days)
    {
        var byDay = _hours.GroupBy(kv => DateTime.UnixEpoch.AddHours(kv.Key).ToLocalTime().Date)
                          .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value.Rejoins));
        var today = DateTime.Now.Date;
        return Enumerable.Range(0, days).Select(i => { var d = today.AddDays(i - (days - 1)); return (d, byDay.GetValueOrDefault(d)); }).ToArray();
    }

    /// Minutes in game per account (by user id) over the last `days` days.
    public Dictionary<long, int> MinutesPerAccount(int days)
    {
        var cutoff = HourOf(DateTime.UtcNow) - days * 24;
        return _hours.Where(kv => kv.Key > cutoff).SelectMany(kv => kv.Value.Minutes)
                     .GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
    }
}
