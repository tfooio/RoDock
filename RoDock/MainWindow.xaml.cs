using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace RoDock;

public record Settings(bool Simple = false, bool Multi = true);

public partial class MainWindow : Window
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoDock");
    static readonly HashSet<string?> Persisted = [nameof(Account.Name), nameof(Account.Cookie), nameof(Account.UserId), nameof(Account.PlaceId), nameof(Account.PrivateServerLink)];

    readonly Store _store;
    readonly string _settingsPath;
    readonly ObservableCollection<Account> _accounts = [];
    readonly Supervisor _supervisor = new();
    readonly DiscordBot _bot;
    readonly string _discordPath;
    DiscordConfig? _discord;
    readonly bool _ready;

    public ObservableCollection<Account> Accounts => _accounts;

    public MainWindow() : this(new Store(Path.Combine(Dir, "accounts.dat")), Path.Combine(Dir, "settings.json")) { }

    public MainWindow(Store store, string settingsPath)
    {
        _store = store;
        _settingsPath = settingsPath;
        _discordPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "discord.dat");
        _bot = new DiscordBot(new Commands(_accounts, _supervisor));
        InitializeComponent();

        try { foreach (var a in _store.Load()) Hook(a); }
        catch (Exception ex)
        {
            // Move the unreadable file aside so the next save can't destroy it.
            Message.Text = $"Could not read saved accounts (kept as .bak): {ex.Message}";
            File.Move(Path.Combine(Dir, "accounts.dat"), Path.Combine(Dir, "accounts.dat.bak"), true);
        }
        Cards.ItemsSource = SimpleView.ItemsSource = _accounts;
        _accounts.CollectionChanged += (_, _) => RefreshSubtitle();

        Settings s = new();
        try { if (File.Exists(_settingsPath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? s; } catch { }
        SimpleBox.IsChecked = s.Simple;
        MultiBox.IsChecked = s.Multi;
        _ready = true;
        try { if (Vault.Read<DiscordConfig>(_discordPath) is { } cfg) _ = ConnectDiscord(cfg); }
        catch (Exception ex) { Message.Text = $"Could not read Discord settings: {ex.Message}"; }
        if (s.Multi) ApplyMulti();
        ApplyMode();
        RefreshSubtitle();
    }

    // ---- accounts ----

    void Hook(Account a)
    {
        a.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Account.PrivateServerLink) && Regex.Match(a.PrivateServerLink, @"/games/(\d+)") is { Success: true } m && long.TryParse(m.Groups[1].Value, out var id))
                a.PlaceId = id; // a private server link belongs to one place, so take the Place ID from it
            if (e.PropertyName == nameof(Account.PlaceId)) _ = LoadGame(a);
            if (e.PropertyName == nameof(Account.Running)) RefreshSubtitle();
            if (Persisted.Contains(e.PropertyName)) _store.Save(_accounts);
        };
        _accounts.Add(a);
        _bot.Watch(a);
        _ = LoadAvatar(a);
        _ = LoadGame(a);
    }

    static async Task LoadAvatar(Account a) => a.AvatarUrl = a.UserId > 0 ? await Roblox.AvatarUrl(a.UserId) : null;

    static async Task LoadGame(Account a)
    {
        var info = a.PlaceId > 0 ? await Roblox.GameInfo(a.PlaceId.Value) : null;
        a.GameName = info?.Name;
        a.GameIcon = info?.Icon;
    }

    void RefreshSubtitle() =>
        Subtitle.Text = $"{_accounts.Count} account{(_accounts.Count == 1 ? "" : "s")}  ·  {_accounts.Count(a => a.Running)} running";

    void Login_Click(object s, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() == true) _ = AddCookie(login.Cookie!);
        else if (login.Error != null) Message.Text = login.Error;
    }

    void Add_Click(object s, RoutedEventArgs e)
    {
        var cookie = CookieBox.Password.Trim();
        if (cookie.Length > 0) _ = AddCookie(cookie);
    }

    async Task AddCookie(string cookie)
    {
        if (cookie.Length > 4096 || cookie.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            Message.Text = "That doesn't look like a .ROBLOSECURITY cookie (it is one line, with no spaces).";
            return;
        }
        try
        {
            var (id, name) = await Roblox.GetUser(cookie);
            if (_accounts.FirstOrDefault(a => a.UserId == id) is { } existing) { existing.Cookie = cookie; existing.Name = name; }
            else Hook(new Account { Name = name, Cookie = cookie, UserId = id });
            _store.Save(_accounts);
            CookieBox.Clear();
            AddPanel.Visibility = Visibility.Collapsed;
            Message.Text = $"Added {name}.";
        }
        catch (Exception ex) { Message.Text = $"Add failed (bad or expired cookie?): {ex.Message}"; }
    }

    void ToggleAdd_Click(object s, RoutedEventArgs e) =>
        AddPanel.Visibility = AddPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    void Remove(Account a) { _supervisor.Stop(a); _accounts.Remove(a); _store.Save(_accounts); }

    // ---- buttons ----

    static Account Of(object sender) => (Account)((FrameworkElement)sender).DataContext;

    void Toggle_Click(object s, RoutedEventArgs e) { var a = Of(s); if (a.Running) _supervisor.Stop(a); else _supervisor.Start(a); }
    void Remove_Click(object s, RoutedEventArgs e) => Remove(Of(s));

    void StartAll_Click(object s, RoutedEventArgs e) { foreach (var a in _accounts) _supervisor.Start(a); }
    void StopAll_Click(object s, RoutedEventArgs e) { foreach (var a in _accounts) _supervisor.Stop(a); }

    IEnumerable<Account> Selected() => SimpleView.SelectedItems.Cast<Account>().ToList();
    void StartSelected_Click(object s, RoutedEventArgs e) { foreach (var a in Selected()) _supervisor.Start(a); }
    void StopSelected_Click(object s, RoutedEventArgs e) { foreach (var a in Selected()) _supervisor.Stop(a); }
    void RemoveSelected_Click(object s, RoutedEventArgs e) { foreach (var a in Selected()) Remove(a); }

    // ---- discord ----

    void ToggleDiscord_Click(object s, RoutedEventArgs e)
    {
        DiscordPanel.Visibility = DiscordPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UserIdBox.Text = _discord?.UserId.ToString() ?? UserIdBox.Text;
        ChannelIdBox.Text = _discord?.ChannelId is > 0 ? _discord.ChannelId.ToString() : ChannelIdBox.Text;
    }

    async void SaveDiscord_Click(object s, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (token.Length == 0) token = _discord?.Token ?? "";
        if (token.Length == 0) { Message.Text = "Paste the bot token first."; return; }
        if (!ulong.TryParse(UserIdBox.Text.Trim(), out var user)) { Message.Text = "Your Discord user ID must be a number."; return; }
        ulong.TryParse(ChannelIdBox.Text.Trim(), out var channel);
        await ConnectDiscord(new DiscordConfig(token, user, channel));
    }

    async Task ConnectDiscord(DiscordConfig cfg)
    {
        try
        {
            await _bot.Connect(cfg);
            Vault.Write(_discordPath, cfg);
            _discord = cfg;
            TokenBox.Clear();
            DiscordPanel.Visibility = Visibility.Collapsed;
            Message.Text = "Discord bot started.";
        }
        catch (Exception ex) { Message.Text = $"Discord failed (wrong token?): {ex.Message}"; }
    }

    // ---- settings ----

    void Mode_Changed(object s, RoutedEventArgs e) { if (_ready) { ApplyMode(); SaveSettings(); } }

    void ApplyMode()
    {
        var simple = SimpleBox.IsChecked == true;
        FullView.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        SimpleView.Visibility = SimpleActions.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        TitleText.FontSize = simple ? 16 : 24;
    }

    void Multi_Changed(object s, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyMulti();
        SaveSettings();
    }

    void ApplyMulti()
    {
        try { Roblox.SetMultiInstance(MultiBox.IsChecked == true); }
        catch (Exception ex) { MultiBox.IsChecked = false; Message.Text = ex.Message; }
    }

    void SaveSettings()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(SimpleBox.IsChecked == true, MultiBox.IsChecked == true))); }
        catch { } // a lost preference is not worth an error dialog
    }
}
