using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RoDock;

public enum Kind { Idle, Working, Good, Warn, Bad }

/// Saved properties are persisted (encrypted); [JsonIgnore] ones are live state shown in the UI.
public class Account : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    string _name = "", _cookie = "", _link = "", _status = "Idle";
    long _userId;
    long? _placeId;
    bool _running;
    Kind _kind;
    string? _avatar, _gameName, _gameIcon, _gameThumb;
    bool _selected;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Cookie { get => _cookie; set => Set(ref _cookie, value); }
    public long UserId { get => _userId; set => Set(ref _userId, value); }
    public long? PlaceId { get => _placeId; set => Set(ref _placeId, value); }
    public string PrivateServerLink { get => _link; set => Set(ref _link, value ?? ""); }

    [JsonIgnore] public string Status { get => _status; private set => Set(ref _status, value); }
    [JsonIgnore] public Kind StatusKind
    {
        get => _kind;
        private set
        {
            if (_kind == value) return;
            Set(ref _kind, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    /// Short state for chips on the dashboard; Status has the full text.
    [JsonIgnore] public string Label => _kind switch { Kind.Good => "In game", Kind.Working => "Launching", Kind.Warn => "Retrying", Kind.Bad => "Needs attention", _ => "Stopped" };
    [JsonIgnore] public bool Running { get => _running; set => Set(ref _running, value); }
    [JsonIgnore] public string? AvatarUrl { get => _avatar; set => Set(ref _avatar, value); }
    [JsonIgnore] public string? GameName { get => _gameName; set => Set(ref _gameName, value); }
    [JsonIgnore] public string? GameIcon { get => _gameIcon; set => Set(ref _gameIcon, value); }
    [JsonIgnore] public string? GameThumb { get => _gameThumb; set => Set(ref _gameThumb, value); }
    [JsonIgnore] public bool Selected { get => _selected; set => Set(ref _selected, value); }

    /// Roblox client processes we started for this account (not saved).
    [JsonIgnore] public int[] Pids = [];

    /// The Roblox log of the client we started (not saved).
    [JsonIgnore] public ClientLog? Log;

    public void Report(string text, Kind kind) { Status = text; StatusKind = kind; Trace(text); }

    static readonly string TracePath = Path.Combine(Environment.GetEnvironmentVariable("RODOCK_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoDock"), "trace.log");
    string _lastTrace = "";

    /// Appends a line to %APPDATA%\RoDock\trace.log (no secrets: status text and process ids) so "why did it relaunch?" can be answered afterwards.
    public void Trace(string text)
    {
        var key = System.Text.RegularExpressions.Regex.Replace(text, @"Retrying in \d+s", "Retrying"); // one line per countdown, not per second
        if (key == _lastTrace) return;
        _lastTrace = key;
        try
        {
            if (File.Exists(TracePath) && new FileInfo(TracePath).Length > 1_000_000) File.Delete(TracePath); // shortcut: wipes instead of rotating
            File.AppendAllText(TracePath, $"{DateTime.UtcNow:HH:mm:ss.fff} {Name}: {text}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // tracing must never break an account
    }
}
