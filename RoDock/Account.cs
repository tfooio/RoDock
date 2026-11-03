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
    string? _avatar, _gameName, _gameIcon;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Cookie { get => _cookie; set => Set(ref _cookie, value); }
    public long UserId { get => _userId; set => Set(ref _userId, value); }
    public long? PlaceId { get => _placeId; set => Set(ref _placeId, value); }
    public string PrivateServerLink { get => _link; set => Set(ref _link, value ?? ""); }

    [JsonIgnore] public string Status { get => _status; private set => Set(ref _status, value); }
    [JsonIgnore] public Kind StatusKind { get => _kind; private set => Set(ref _kind, value); }
    [JsonIgnore] public bool Running { get => _running; set => Set(ref _running, value); }
    [JsonIgnore] public string? AvatarUrl { get => _avatar; set => Set(ref _avatar, value); }
    [JsonIgnore] public string? GameName { get => _gameName; set => Set(ref _gameName, value); }
    [JsonIgnore] public string? GameIcon { get => _gameIcon; set => Set(ref _gameIcon, value); }

    /// Roblox client processes we started for this account (not saved).
    [JsonIgnore] public int[] Pids = [];

    public void Report(string text, Kind kind) { Status = text; StatusKind = kind; }
}
