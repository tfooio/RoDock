using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RoDock;

/// Resume = user ids of accounts that were running when an update restarted the app; they are started again on the next launch.
/// ThemeName = the chosen theme; Themes = only the themes that differ from the built-in ones (edited built-ins and the user's own).
public record Settings(bool Simple = false, bool Multi = true, long[]? Resume = null, string? ThemeName = null, Theme[]? Themes = null, bool Animations = true);

public record ThemeChip(string Name, Brush A, Brush B, Brush C, bool Selected);
public record SlotVm(string Key, string Label, Brush Fill, Brush Ink, string Hex);

/// One line of the activity list; StatusKind picks the dot colour.
public record FeedItem(string Text, string Time, Kind StatusKind);
public record Rank(string Name, string Hours, double Share, Brush Fill);
public record StatVm(string Label, string Value, string Note);

public partial class MainWindow : Window
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoDock");
    static readonly HashSet<string?> Persisted = [nameof(Account.Name), nameof(Account.Cookie), nameof(Account.UserId), nameof(Account.PlaceId), nameof(Account.PrivateServerLink)];

    readonly Store _store;
    readonly string _settingsPath;
    readonly ObservableCollection<Account> _accounts = [];
    readonly Supervisor _supervisor = new();
    readonly Updater _updater = new();
    readonly System.Windows.Threading.DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(4) };
    readonly DiscordBot _bot;
    readonly string _discordPath;
    DiscordConfig? _discord;
    readonly bool _ready;
    bool _revertingMulti, _multiWanted;
    readonly History _history;
    readonly ObservableCollection<FeedItem> _feed = [];
    readonly System.Windows.Threading.DispatcherTimer _minute = new() { Interval = TimeSpan.FromMinutes(1) };
    Account? _selected;
    List<Theme> _themes = Themes.Merge(null);
    Theme _theme = Themes.Presets[0];
    string _slot = "Bg";

    public ObservableCollection<Account> Accounts => _accounts;

    public MainWindow() : this(new Store(Path.Combine(Dir, "accounts.dat")), Path.Combine(Dir, "settings.json")) { }

    public MainWindow(Store store, string settingsPath)
    {
        _store = store;
        _settingsPath = settingsPath;
        _discordPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "discord.dat");
        _bot = new DiscordBot(new Commands(_accounts, _supervisor));
        _history = new History(Path.Combine(Path.GetDirectoryName(settingsPath)!, "history.json"));
        InitializeComponent();

        // Entrances: whatever becomes visible eases in. Popovers grow from 0.95 at their trigger instead of rising.
        foreach (UIElement p in new UIElement[] { DashView, EmptyView, SimpleView, ThemePanel, AddPanel, DiscordPanel, RobloxSettingsPanel, UpdateToast })
            p.IsVisibleChanged += (s, e) => { if (e.NewValue is true) RoDock.Animations.Enter((UIElement)s); };
        HeroSettings.IsVisibleChanged += (_, e) => { if (e.NewValue is true) RoDock.Animations.Enter(HeroSettings, rise: 0, scale: .95, origin: new Point(1, 0)); };
        PickerPop.Opened += (_, _) => RoDock.Animations.Enter(Picker, rise: 0, scale: .95, origin: new Point(.5, 0));

        try { foreach (var a in _store.Load()) Hook(a); }
        catch (Exception ex)
        {
            // Move the unreadable file aside so the next save can't destroy it.
            Message.Text = $"Could not read saved accounts (kept as .bak): {ex.Message}";
            File.Move(Path.Combine(Dir, "accounts.dat"), Path.Combine(Dir, "accounts.dat.bak"), true);
        }
        Cards.ItemsSource = SimpleView.ItemsSource = DockList.ItemsSource = _accounts;
        Feed.ItemsSource = _feed;
        _accounts.CollectionChanged += (_, _) => OnAccountsChanged();
        _feed.CollectionChanged += (_, _) => FeedEmpty.Visibility = _feed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Settings s = new();
        try { if (File.Exists(_settingsPath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? s; } catch { }
        _themes = Themes.Merge(s.Themes);
        _theme = _themes.FirstOrDefault(t => t.Name == s.ThemeName) ?? _themes[0];
        Themes.Apply(_theme);
        RefreshThemeUi();
        Picker.Changed += c => UpdateTheme(_theme.With(_slot, $"#{c.R:X2}{c.G:X2}{c.B:X2}"));
        ListRadio.IsChecked = s.Simple;
        CardsRadio.IsChecked = !s.Simple;
        MultiBox.IsChecked = _multiWanted = s.Multi;
        RoDock.Animations.State.On = s.Animations;
        _ready = true;
        RoDock.Animations.State.PropertyChanged += (_, _) => SaveSettings();
        try { if (Vault.Read<DiscordConfig>(_discordPath) is { } cfg) _ = ConnectDiscord(cfg); }
        catch (Exception ex) { Message.Text = $"Could not read Discord settings: {ex.Message}"; }
        if (s.Multi) ApplyMulti();
        OnAccountsChanged();

        // Once a minute: count who is in game for the charts.
        _minute.Tick += (_, _) =>
        {
            foreach (var a in _accounts.Where(a => a.StatusKind == Kind.Good)) _history.InGameMinute(a.UserId);
            _history.Save();
            RefreshCharts();
        };
        _minute.Start();
        Closed += (_, _) => _history.Save();

        if (s.Resume is { Length: > 0 })
        {
            foreach (var a in _accounts.Where(a => s.Resume.Contains(a.UserId))) _supervisor.Start(a);
            SaveSettings(); // clears the resume list
        }

        _updateTimer.Tick += async (_, _) => await CheckUpdate();
        _updateTimer.Start();
        _ = CheckUpdate(); // also at every launch
    }

    // ---- title bar (the window has no native one) ----

    void Minimize_Click(object s, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    void Close_Click(object s, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    void Maximize_Click(object s, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this);
    }

    void Window_StateChanged(object? s, EventArgs e)
    {
        var max = WindowState == WindowState.Maximized;
        // A maximised window without a native frame overhangs the screen by its resize border; pull the content back in.
        RootFrame.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaxGlyph.Data = Geometry.Parse(max ? "M2.5,2.5 L2.5,0.5 L9.5,0.5 L9.5,7.5 L7.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z" : "M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z");
        MaxButton.ToolTip = max ? "Restore" : "Maximize";
    }

    // ---- updates ----

    async Task CheckUpdate()
    {
        if (await _updater.Check() is not { } version) return;
        UpdateText.Text = $"Update to {version}";
        UpdateToast.Visibility = Visibility.Visible;
    }

    async void Update_Click(object s, RoutedEventArgs e)
    {
        UpdateToast.IsEnabled = false;
        UpdateText.Text = "Downloading update...";
        try
        {
            await _updater.Download(); // accounts keep running while this downloads
            var running = _accounts.Where(a => a.Running).ToList();
            SaveSettings(running.Select(a => a.UserId).ToArray());
            await Task.WhenAll(running.Select(_supervisor.Stop)); // close their clients so none are orphaned by the restart
            _updater.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            UpdateToast.IsEnabled = true;
            UpdateText.Text = "Update failed";
            Message.Text = $"Update failed: {ex.Message}";
        }
    }

    // ---- accounts ----

    void Hook(Account a)
    {
        var last = a.StatusKind;
        a.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Account.StatusKind))
            {
                // Dropped (or retrying) and now launching again = a rejoin.
                if (a.StatusKind == Kind.Working && last is Kind.Good or Kind.Warn) { _history.Rejoin(); RefreshCharts(); }
                last = a.StatusKind;
                _feed.Insert(0, new FeedItem($"{a.Name}: {a.Status}", DateTime.Now.ToString("HH:mm"), a.StatusKind));
                if (_feed.Count > 30) _feed.RemoveAt(30);
                RefreshSubtitle();
            }
            if (e.PropertyName == nameof(Account.PrivateServerLink) && Regex.Match(a.PrivateServerLink, @"/games/(\d+)") is { Success: true } m && long.TryParse(m.Groups[1].Value, out var id))
                a.PlaceId = id; // a private server link belongs to one place, so take the Place ID from it
            if (e.PropertyName == nameof(Account.PrivateServerLink) && Roblox.ShareCode(a.PrivateServerLink) is { } share) _ = ResolveShare(a, share);
            if (e.PropertyName == nameof(Account.PlaceId)) _ = LoadGame(a);
            if (e.PropertyName == nameof(Account.Running)) RefreshSubtitle();
            if (Persisted.Contains(e.PropertyName)) _store.Save(_accounts);
        };
        _accounts.Add(a);
        _bot.Watch(a);
        _ = LoadAvatar(a);
        _ = LoadGame(a);
    }

    // Share links don't say which game they are for; ask Roblox so the card can show it.
    async Task ResolveShare(Account a, string shareCode)
    {
        try { a.PlaceId = (await Roblox.ResolveShareLink(a.Cookie, shareCode)).PlaceId; }
        catch (Exception ex) { Message.Text = ex.Message; }
    }

    static async Task LoadAvatar(Account a) => a.AvatarUrl = a.UserId > 0 ? await Roblox.AvatarUrl(a.UserId) : null;

    static async Task LoadGame(Account a)
    {
        var info = a.PlaceId > 0 ? await Roblox.GameInfo(a.PlaceId.Value) : null;
        a.GameName = info?.Name;
        a.GameIcon = info?.Icon;
        a.GameThumb = info?.Thumb;
    }

    void OnAccountsChanged()
    {
        if (_selected is null || !_accounts.Contains(_selected)) Select(_accounts.FirstOrDefault());
        RefreshSubtitle();
        RefreshView();
        RefreshCharts();
    }

    void Select(Account? a)
    {
        foreach (var x in _accounts) x.Selected = x == a;
        _selected = a;
        Hero.DataContext = a;
        HeroGear.IsChecked = false;
    }

    void Select_Click(object s, MouseButtonEventArgs e) => Select(Of(s));

    void RefreshSubtitle()
    {
        var inGame = _accounts.Count(a => a.StatusKind == Kind.Good);
        GreetRun.Text = "Docks";
        CountRun.Text = _accounts.Count == 0 ? "" : $"   {inGame} of {_accounts.Count} in game";
        DockCount.Text = $"{_accounts.Count} account{(_accounts.Count == 1 ? "" : "s")}";
        LineNote.Text = $"Now {inGame} of {_accounts.Count}";
        RefreshCharts(); // the four numbers follow the live status
    }

    // ---- themes ----

    static Brush Solid(string hex) => new SolidColorBrush(Themes.Parse(hex));

    void RefreshThemeUi(bool slots = true)
    {
        ThemeList.ItemsSource = _themes.Select(t => new ThemeChip(t.Name, Solid(t.Bg), Solid(t.Card), Solid(t.Accent), t.Name == _theme.Name)).ToList();
        if (slots)
        {
            string[] labels = ["Background", "Panels", "Rails & hero", "Accent", "Text"];
            Slots.ItemsSource = Theme.Slots.Select((k, i) =>
            {
                var hex = _theme.Get(k);
                return new SlotVm(k, labels[i], Solid(hex), Themes.Luma(Themes.Parse(hex)) > 0.55 ? Brushes.Black : Brushes.White, hex.ToUpperInvariant());
            }).ToList();
        }
        var preset = Themes.Presets.FirstOrDefault(p => p.Name == _theme.Name);
        ThemeReset.IsEnabled = preset is not null && preset != _theme;
        ThemeDelete.IsEnabled = preset is null;
    }

    void ApplyTheme(Theme t, bool slots = true)
    {
        _theme = t;
        Themes.Apply(t);
        RefreshThemeUi(slots);
        RefreshCharts(); // the bars hold brushes of their own
    }

    /// An edit of the current theme (live, while the picker is being dragged). The swatches are refreshed when the picker closes.
    void UpdateTheme(Theme t)
    {
        _themes[_themes.FindIndex(x => x.Name == t.Name)] = t;
        ApplyTheme(t, slots: false);
    }

    static Theme Named(object sender, List<Theme> all) => all.First(t => t.Name == ((ThemeChip)((FrameworkElement)sender).DataContext).Name);

    void ThemePick_Click(object s, RoutedEventArgs e) { ApplyTheme(Named(s, _themes)); SaveSettings(); }

    void Slot_Click(object s, RoutedEventArgs e)
    {
        _slot = ((SlotVm)((FrameworkElement)s).DataContext).Key;
        Picker.SetColor(Themes.Parse(_theme.Get(_slot)));
        PickerPop.PlacementTarget = (UIElement)s;
        PickerPop.IsOpen = true;
    }

    void PickerPop_Closed(object? s, EventArgs e) { RefreshThemeUi(); SaveSettings(); }

    void ThemeNew_Click(object s, RoutedEventArgs e)
    {
        var n = 1;
        while (_themes.Any(t => t.Name == (n == 1 ? "Custom" : $"Custom {n}"))) n++;
        var copy = _theme with { Name = n == 1 ? "Custom" : $"Custom {n}" };
        _themes.Add(copy);
        ApplyTheme(copy);
        SaveSettings();
    }

    void ThemeReset_Click(object s, RoutedEventArgs e)
    {
        var preset = Themes.Presets.First(p => p.Name == _theme.Name);
        _themes[_themes.FindIndex(x => x.Name == preset.Name)] = preset;
        ApplyTheme(preset);
        SaveSettings();
    }

    void ThemeDelete_Click(object s, RoutedEventArgs e)
    {
        if (Themes.Presets.Any(p => p.Name == _theme.Name)) return;
        _themes.RemoveAll(t => t.Name == _theme.Name);
        ApplyTheme(_themes[0]);
        SaveSettings();
    }

    // ---- charts ----

    void Chart_SizeChanged(object s, SizeChangedEventArgs e) => RefreshCharts();

    void RefreshCharts()
    {
        var inGame = _accounts.Count(a => a.StatusKind == Kind.Good);
        var max = Math.Max(1, _accounts.Count);

        // Main chart: accounts in game, hourly average over the last 24 h. A count jumps, so it is drawn as steps.
        var hourly = _history.Hourly(24);
        double w = LineCanvas.ActualWidth, h = LineCanvas.ActualHeight;
        if (w > 0 && h > 0)
        {
            double X(int i) => i * w / (hourly.Length - 1);
            double Y(double v) => h - 3 - Math.Min(v, max) / max * (h - 6);
            var pts = new List<Point> { new(0, Y(hourly[0])) };
            for (var i = 1; i < hourly.Length; i++) { pts.Add(new(X(i), Y(hourly[i - 1]))); pts.Add(new(X(i), Y(hourly[i]))); }
            LineLine.Points = new PointCollection(pts);
            LineArea.Points = new PointCollection(pts.Append(new Point(w, h)).Append(new Point(0, h)));
        }

        // Ranked list: hours in game per account over 7 days. The leader gets the accent, the rest a neutral step.
        var minutes = _history.MinutesPerAccount(7).Where(kv => _accounts.Any(a => a.UserId == kv.Key) && kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
        var total = minutes.Sum(kv => kv.Value);
        Brush accent = (Brush)FindResource("Accent"), neutral = (Brush)FindResource("Neutral");
        Ranks.ItemsSource = minutes.Select((kv, i) => new Rank(_accounts.First(a => a.UserId == kv.Key).Name, $"{kv.Value / 60.0:0.#} h", (double)kv.Value / minutes[0].Value, i == 0 ? accent : neutral)).ToList();
        RankTotal.Text = $"{total / 60.0:0.#} h, 7 days";

        // Four numbers.
        var rejoins = _history.RejoinsPerDay(7).Sum(d => d.Rejoins);
        var parts = new List<string>();
        var launching = _accounts.Count(a => a.StatusKind == Kind.Working);
        var retrying = _accounts.Count(a => a.Running && a.StatusKind is Kind.Warn or Kind.Bad);
        var stopped = _accounts.Count(a => !a.Running);
        if (launching > 0) parts.Add($"{launching} launching");
        if (retrying > 0) parts.Add($"{retrying} retrying");
        if (stopped > 0) parts.Add($"{stopped} stopped");
        Stats.ItemsSource = new[]
        {
            new StatVm("In game now", $"{inGame} of {_accounts.Count}", parts.Count > 0 ? string.Join(", ", parts) : "All running"),
            new StatVm("In game, 24 hours", $"{Math.Round(100 * hourly.Average() / max)}%", "Average across accounts"),
            new StatVm("Rejoins, 7 days", rejoins.ToString(), $"{rejoins / 7.0:0.#} a day"),
            new StatVm("Time online, 7 days", $"{total / 60.0:0.#} h", $"{minutes.Count} account{(minutes.Count == 1 ? "" : "s")}"),
        };
    }

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
    async void Rejoin_Click(object s, RoutedEventArgs e) { var a = Of(s); await _supervisor.Stop(a); _supervisor.Start(a); }
    void Remove_Click(object s, RoutedEventArgs e) => Remove(Of(s));

    void StartAll_Click(object s, RoutedEventArgs e) { foreach (var a in _accounts) _supervisor.Start(a); }
    void StopAll_Click(object s, RoutedEventArgs e) { foreach (var a in _accounts) _supervisor.Stop(a); }

    IEnumerable<Account> Ticked() => SimpleView.SelectedItems.Cast<Account>().ToList();
    void StartSelected_Click(object s, RoutedEventArgs e) { foreach (var a in Ticked()) _supervisor.Start(a); }
    void StopSelected_Click(object s, RoutedEventArgs e) { foreach (var a in Ticked()) _supervisor.Stop(a); }
    void RemoveSelected_Click(object s, RoutedEventArgs e) { foreach (var a in Ticked()) Remove(a); }

    // ---- roblox settings ----

    void ToggleRobloxSettings_Click(object s, RoutedEventArgs e)
    {
        RobloxSettingsPanel.Visibility = RobloxSettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ShowRobloxSettingsState();
    }

    void ShowRobloxSettingsState() => RobloxSettingsState.Text = RobloxSettings.Default.HasSaved
        ? "Saved settings are put back before every launch."
        : "Nothing saved yet, so Roblox uses its own settings.";

    void SaveRobloxSettings_Click(object s, RoutedEventArgs e)
    {
        try { RobloxSettings.Default.SaveCurrent(); Message.Text = "Saved. These Roblox settings are put back before every launch."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message.Text = ex.Message; }
        ShowRobloxSettingsState();
    }

    void ClearRobloxSettings_Click(object s, RoutedEventArgs e)
    {
        try { RobloxSettings.Default.Clear(); Message.Text = "Stopped. Roblox uses its own settings again."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message.Text = ex.Message; }
        ShowRobloxSettingsState();
    }

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
            DiscordDot.Fill = (Brush)FindResource("Good");
            TokenBox.Clear();
            DiscordPanel.Visibility = Visibility.Collapsed;
            Message.Text = "Discord bot started.";
        }
        catch (Exception ex) { Message.Text = $"Discord failed (wrong token?): {ex.Message}"; }
    }

    // ---- settings ----

    /// Dock tiles and activity rows ease in when created; tiles that appear together are staggered 30 ms apart (the first eight).
    void Row_Loaded(object s, RoutedEventArgs e)
    {
        var el = (FrameworkElement)s;
        RoDock.Animations.Enter(el, el.DataContext is Account a ? Math.Clamp(_accounts.IndexOf(a), 0, 8) * 30 : 0);
    }

    void Mode_Changed(object s, RoutedEventArgs e) { if (_ready) { RefreshView(); SaveSettings(); } }

    /// Dashboard, first-run screen (no accounts) or the plain list.
    void RefreshView()
    {
        var simple = ListRadio.IsChecked == true;
        var empty = _accounts.Count == 0;
        DashView.Visibility = !simple && !empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyView.Visibility = !simple && empty ? Visibility.Visible : Visibility.Collapsed;
        SimpleView.Visibility = SimpleActions.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        TitleText.FontSize = simple ? 20 : 28;
    }

    void Multi_Changed(object s, RoutedEventArgs e)
    {
        if (!_ready || _revertingMulti) return;
        _multiWanted = MultiBox.IsChecked == true;
        ApplyMulti();
        SaveSettings();
    }

    /// If the lock can't be taken (another app or an open Roblox holds it) the box is unticked, but that is not saved:
    /// the preference stays "on" for the next launch.
    void ApplyMulti()
    {
        try { Roblox.SetMultiInstance(MultiBox.IsChecked == true); }
        catch (Exception ex)
        {
            _revertingMulti = true;
            MultiBox.IsChecked = false;
            _revertingMulti = false;
            Message.Text = ex.Message;
        }
    }

    void SaveSettings(long[]? resume = null)
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(ListRadio.IsChecked == true, _multiWanted, resume, _theme.Name, _themes.Where(t => !Themes.Presets.Contains(t)).ToArray(), RoDock.Animations.State.On))); }
        catch { } // a lost preference is not worth an error dialog
    }
}
