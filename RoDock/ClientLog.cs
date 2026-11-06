using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoDock;

public enum LogKind { Join, Failure }

/// Failure carries Roblox's error number when the log line has one.
public record LogEvent(LogKind Kind, int? Code, long? UserId = null);

/// Reads one Roblox client's own log file, so errors show up in seconds instead of waiting for Roblox's online status to time out.
public class ClientLog
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    // Patterns taken from real client logs. Harmless noise like "SignalRCoreError ... Disconnected - stop() called" must not match.
    static readonly Regex Line = new(
        @"(?<join>! Joining game )|game_join_loadtime:.*?userid:(?<uid>\d+)|Connection error (?<c1>\d+)|Disconnected from server for reason: \w+: (?<c2>\d+)|Sending disconnect with reason: (?<c3>\d+)|(?<fail>Connection attempt failed|Session Transition FSM: Error Occurred)",
        RegexOptions.Compiled);

    readonly string _path;
    long _pos;

    internal ClientLog(string path) => _path = path;

    static readonly TimeSpan TeleportWindow = TimeSpan.FromSeconds(15);
    DateTime _lastJoin = DateTime.MinValue;

    /// A teleport between a game's places logs the new join first and, ~0.1 s later, the old connection closing as error 285
    /// ("client initiated"). Left in, that read as a failure after the join and RoDock closed a healthy client 25 s later.
    internal List<LogEvent> DropTeleportNoise(IEnumerable<LogEvent> events, DateTime now)
    {
        var kept = new List<LogEvent>();
        foreach (var e in events)
        {
            if (e.Kind == LogKind.Join) _lastJoin = now;
            else if (e.Code == 285 && now - _lastJoin < TeleportWindow) continue;
            kept.Add(e);
        }
        return kept;
    }

    public static IEnumerable<LogEvent> Parse(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var m = Line.Match(line);
            if (!m.Success) continue;
            if (m.Groups["join"].Success) { yield return new LogEvent(LogKind.Join, null); continue; }
            if (m.Groups["uid"].Success) { yield return new LogEvent(LogKind.Join, null, long.Parse(m.Groups["uid"].Value)); continue; } // says whose join it was
            var code = new[] { "c1", "c2", "c3" }.Select(g => m.Groups[g]).FirstOrDefault(g => g.Success);
            yield return new LogEvent(LogKind.Failure, code is null ? null : int.Parse(code.Value));
        }
    }

    /// The log of a client we started: the file whose start time (it is in the file name) is closest to the process start.
    public static ClientLog? For(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var started = p.StartTime.ToUniversalTime();
            return Directory.EnumerateFiles(Dir, "*_Player_*_last.log")
                .Where(f => !f.Contains("CrashHandler"))
                .Select(f => (File: f, Gap: Stamp(f) is { } t ? Math.Abs((t - started).TotalSeconds) : double.MaxValue))
                .Where(x => x.Gap < 20)
                .OrderBy(x => x.Gap)
                .Select(x => new ClientLog(x.File))
                .FirstOrDefault();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return null; }
    }

    static DateTime? Stamp(string file) =>
        Regex.Match(file, @"_(\d{8}T\d{6})Z_Player_") is { Success: true } m
        && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    /// New events since the last call. Only whole lines are consumed, so a line still being written is read next time.
    public List<LogEvent> Poll()
    {
        try
        {
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length <= _pos) return [];
            fs.Seek(_pos, SeekOrigin.Begin);
            var bytes = new byte[fs.Length - _pos];
            var read = fs.Read(bytes, 0, bytes.Length);
            var end = Array.LastIndexOf(bytes, (byte)'\n', read - 1);
            if (end < 0) return [];
            _pos += end + 1;
            return DropTeleportNoise(Parse(Encoding.UTF8.GetString(bytes, 0, end + 1)), DateTime.UtcNow);
        }
        catch (IOException) { return []; } // locked or gone for a moment; try again on the next tick
    }
}
