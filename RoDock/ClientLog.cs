using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoDock;

public enum LogKind { Join, Failure }

/// Failure carries Roblox's error number when the log line has one.
public record LogEvent(LogKind Kind, int? Code);

/// Reads one Roblox client's own log file, so errors show up in seconds instead of waiting for Roblox's online status to time out.
public class ClientLog
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    // Patterns taken from real client logs. Harmless noise like "SignalRCoreError ... Disconnected - stop() called" must not match.
    static readonly Regex Line = new(
        @"(?<join>! Joining game )|Connection error (?<c1>\d+)|Disconnected from server for reason: \w+: (?<c2>\d+)|Sending disconnect with reason: (?<c3>\d+)|(?<fail>Connection attempt failed|Session Transition FSM: Error Occurred)",
        RegexOptions.Compiled);

    readonly string _path;
    long _pos;

    ClientLog(string path) => _path = path;

    public static IEnumerable<LogEvent> Parse(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var m = Line.Match(line);
            if (!m.Success) continue;
            if (m.Groups["join"].Success) { yield return new LogEvent(LogKind.Join, null); continue; }
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
            return Parse(Encoding.UTF8.GetString(bytes, 0, end + 1)).ToList();
        }
        catch (IOException) { return []; } // locked or gone for a moment; try again on the next tick
    }
}
