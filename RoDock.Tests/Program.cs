// No test framework needed.
//   dotnet run --project RoDock.Tests                 -> self-checks (exit code 1 on failure)
//   dotnet run --project RoDock.Tests -- preview DIR  -> renders both UI modes to DIR/*.png (no Roblox, no accounts, no login)
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RoDock;

// App.xaml's StartupUri opens a real window in these modes; keep it on a scratch data folder, away from the user's accounts and settings (Motion is toggled below, which saves settings).
if (args is ["preview", _] or ["motion-check"] or ["fps-probe"]) Environment.SetEnvironmentVariable("RODOCK_DATA", Path.Combine(Path.GetTempPath(), "rl-scratch-" + Guid.NewGuid()));
if (args is ["preview", var outDir]) { RunPreview(outDir); return; }
if (args is ["icon", var iconDir]) { IconMaker.Run(iconDir); return; }
if (args is ["login-check"]) { RunLoginCheck(); return; }
if (args is ["multi-check"]) { RunMultiCheck(); return; }
if (args is ["close-check"]) { RunCloseCheck(); return; }
if (args is ["cookie-check"]) { await RunCookieCheck(); return; }
if (args is ["settings-check"]) { RunSettingsCheck(); return; }
if (args is ["anim-check", var mode]) { RunAnimCheck(mode == "sw"); return; }
if (args is ["motion-check"]) { RunMotionCheck(); return; }
if (args is ["fps-probe"]) { RunFpsProbe(); return; }

void Check(bool ok, string what) { if (!ok) { Console.WriteLine("FAIL: " + what); Environment.Exit(1); } }

// Store: encrypted on disk, round-trips saved fields, never persists live state.
var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
var store = new Store(path);
var acc = new Account { Name = "alt1", Cookie = "SECRET-COOKIE", UserId = 7, PlaceId = 123, PrivateServerLink = "https://www.roblox.com/games/123/x?privateServerLinkCode=ABC" };
acc.Report("In game", Kind.Good);
store.Save([acc]);
Check(!File.ReadAllText(path).Contains("SECRET-COOKIE"), "store file must not contain plaintext cookie");
var back = store.Load().Single();
Check(back.Name == "alt1" && back.Cookie == "SECRET-COOKIE" && back.UserId == 7 && back.PlaceId == 123 && back.PrivateServerLink.Contains("ABC"), "store round-trip");
Check(back.Status == "Idle" && !back.Running, "live state must not be persisted");
File.Delete(path);

// History (dashboard charts): minutes and rejoins add up per hour/day/account, survive a save and reload, and old hours are dropped.
var hpath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
var hist0 = new History(hpath);
hist0.InGameMinute(7); hist0.InGameMinute(7); hist0.InGameMinute(8); hist0.Rejoin();
hist0.Add(DateTime.UtcNow.AddDays(-20), 7, 500, 9); // too old to keep
hist0.Save();
var hist1 = new History(hpath);
Check(hist1.MinutesPerAccount(7)[7] == 2 && hist1.MinutesPerAccount(7)[8] == 1, "history: minutes per account survive a reload");
Check(hist1.RejoinsPerDay(7)[^1].Rejoins == 1 && hist1.RejoinsPerDay(7).Length == 7, "history: today's rejoin counted, 7 days returned");
Check(hist1.Hourly(24).Length == 24 && hist1.Hourly(24)[^1] > 0 && hist1.Hourly(24)[0] == 0, "history: hourly series ends with the current hour");
Check(!hist1.MinutesPerAccount(30).ContainsKey(99) && hist1.MinutesPerAccount(30)[7] == 2, "history: entries older than 8 days are dropped on save");
File.Delete(hpath);

// Themes: built-ins come first; a saved edit replaces its built-in, other saved themes follow; settings round-trip through JSON.
var edited = Themes.Presets[3] with { Bg = "#000000" };
var own = new Theme("Custom", "#102A43", "#1B3B5A", "#0B1F33", "#FF7A59", "#FFFFFF");
var merged = Themes.Merge([own, edited]);
Check(merged.Count == Themes.Presets.Length + 1 && merged[3].Bg == "#000000" && merged[^1].Name == "Custom" && merged[0] == Themes.Presets[0], "themes: edits replace built-ins, own themes are appended");
Check(Themes.Merge(null).SequenceEqual(Themes.Presets), "themes: nothing saved = the built-ins");
var json = System.Text.Json.JsonSerializer.Serialize(new Settings(false, true, null, "Custom", [own]));
var settingsBack = System.Text.Json.JsonSerializer.Deserialize<Settings>(json)!;
Check(settingsBack.ThemeName == "Custom" && settingsBack.Themes![0] == own, "themes: settings round-trip");
Check(System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Simple\":true}")!.ThemeName is null, "themes: old settings files still load");
Check(own.With("Accent", "#123456").Accent == "#123456" && own.With("Text", "#ABCDEF").Get("Text") == "#ABCDEF", "themes: With/Get address the right colour");

// Launch URIs.
var plain = Roblox.BuildLaunchUri("TICKET", 123, null, null, "999", 1);
var priv = Roblox.BuildLaunchUri("TICKET", 123, "aaaa-bbbb", "LINK", "999", 1);
Check(plain.Contains("gameinfo:TICKET") && plain.Contains("request%3DRequestGame%26"), "plain launch URI");
Check(priv.Contains("RequestPrivateGame") && priv.Contains("accessCode%3Daaaa-bbbb") && priv.Contains("linkCode%3DLINK"), "private launch URI");

// Supervisor refuses to run an account without a Place ID.
var sup = new Supervisor();
var noPlace = new Account { Name = "x", Cookie = "c" };
sup.Start(noPlace);
await Task.Delay(500);
Check(!noPlace.Running && noPlace.StatusKind == Kind.Bad, "supervisor needs a Place ID");

// Discord command logic (no Discord needed).
var accs = new List<Account> { new() { Name = "Alpha", Cookie = "c" }, new() { Name = "Beta", Cookie = "c" } };
var cmds = new Commands(accs, new Supervisor());
Check((await cmds.Handle("status")).Contains("**Alpha**") && (await cmds.Handle("status")).Contains("**Beta**"), "/status lists accounts");
Check((await cmds.Handle("start", "nobody")).StartsWith("No account named"), "/start unknown account");
Check((await cmds.Handle("start", " alpha ")).Contains("Starting **Alpha**"), "/start matches name ignoring case and spaces");
Check((await cmds.Handle("stop", "Beta")).Contains("Stopped **Beta**"), "/stop");
Check((await cmds.Handle("nonsense", "Beta")).StartsWith("Unknown command"), "unknown command");

// New-style private server share links are recognised; other links are not mistaken for them.
Check(Roblox.ShareCode("https://www.roblox.com/share?code=0ac3ad5cec2742449f1b430f67255478&type=Server") == "0ac3ad5cec2742449f1b430f67255478", "share link code");
Check(Roblox.ShareCode("https://www.roblox.com/share?type=Server&code=abcdef0123456789") == "abcdef0123456789", "share link, params in other order");
Check(Roblox.ShareCode("https://www.roblox.com/games/123/x?privateServerLinkCode=ABC") is null, "old-style link is not a share link");
Check(Roblox.ShareCode("https://www.roblox.com/share?code=0ac3ad5cec2742449f1b430f67255478&type=ExperienceDetails") is null, "non-server share link ignored");

Check(!Roblox.IsProtocolRegistered("roblox-no-such-scheme-xyz"), "unregistered protocol is reported as missing");

// Roblox error numbers: the table covers the official list and picks the right action.
Check(RobloxErrors.Count == 97, "error table has every item of the official list except OK (97)");
Check(RobloxErrors.Describe(2) == ("Error 2 (ConnectErrors)", Act.Retry), "error 2 = connect error, retry");
Check(RobloxErrors.Describe(277).Act == Act.Retry && RobloxErrors.Describe(278).Act == Act.Retry, "connection lost and idle kick are retried");
Check(RobloxErrors.Describe(273).Act == Act.Slow && RobloxErrors.Describe(275).Act == Act.Slow, "same-account and maintenance wait longer");
Check(new[] { 5, 286, 287, 515, 523, 524, 600, 770, 773 }.All(c => RobloxErrors.Describe(c).Act == Act.Stop), "bans, moderation, revoked or missing games stop");
Check(RobloxErrors.Describe(9999).Act == Act.Retry && RobloxErrors.Describe(null).Act == Act.Retry, "unknown or missing number retries");

// Real lines from a Roblox client log (the "Error Code: 2" case): the failure is found, and ordinary noise is ignored.
const string sample = """
    2026-11-04T01:07:19.286Z,1.0,1,6 [FLog::Output] ! Joining game 'g' place 8304191830 at 1.2.3.4
    2026-11-04T01:07:19.378Z,1.0,1,6 [DFLog::SignalRCoreError] ID: 1 Disconnected - stop() called
    2026-11-04T01:07:34.581Z,1.0,1,6 [FLog::Network] Connection attempt failed
    2026-11-04T01:07:34.582Z,1.0,1,6 [DFLog::NetworkClient] Server reached at 1.2.3.4|51918, but handshake failed. Connection error 2
    2026-11-04T01:07:34.593Z,1.0,1,6 [FLog::Output] Session Transition FSM: Error Occurred.
    2026-11-04T01:08:26.928Z,1.0,1,6 [DFLog::RbxTransportDummyClient] Disconnected from server for reason: Player: 285 (DisconnectClientInitiated)
    2026-11-04T01:09:00.000Z,1.0,1,6 [FLog::Network] Sending disconnect with reason: 285
    """;
var events = ClientLog.Parse(sample).ToList();
Check(events.Count == 6 && events[0].Kind == LogKind.Join, "log: join found, SignalR noise ignored");
Check(events[2] == new LogEvent(LogKind.Failure, 2) && events[1] == new LogEvent(LogKind.Failure, null), "log: 'Connection error 2' read as code 2");
Check(events[4] == new LogEvent(LogKind.Failure, 285) && events[5] == new LogEvent(LogKind.Failure, 285), "log: disconnect reasons read as codes");
Check(Supervisor.FirstFailure(events.Skip(1))!.Reason == "Error 2 (ConnectErrors)", "failure picks the first numbered error");
Check(Supervisor.FirstFailure(events.Take(1)) is null, "a join alone is not a failure");

// Roblox settings keeper (temp files, never the real ones).
{
    var dir = Path.Combine(Path.GetTempPath(), "rodock-settings-" + Guid.NewGuid());
    Directory.CreateDirectory(dir);
    var roblox = Path.Combine(dir, "GlobalBasicSettings_13.xml");
    var kept = Path.Combine(dir, "saved", "roblox-settings.xml");
    const string mine = "<roblox><Item><Properties><int name=\"GraphicsQualityLevel\">3</int><int name=\"FramerateCap\">60</int></Properties></Item></roblox>";
    const string defaults = "<roblox><Item><Properties><int name=\"GraphicsQualityLevel\">0</int><int name=\"FramerateCap\">-1</int></Properties></Item></roblox>";
    var keeper = new RobloxSettings(roblox, kept);

    Check(!keeper.Apply() && !File.Exists(roblox), "apply with nothing saved does nothing");
    try { keeper.SaveCurrent(); Check(false, "saving with no Roblox file must fail"); } catch (FileNotFoundException) { }

    File.WriteAllText(roblox, mine);
    keeper.SaveCurrent();
    Check(keeper.HasSaved && File.ReadAllText(kept) == mine, "save copies the current Roblox settings");

    File.WriteAllText(roblox, defaults); // Roblox resets them when a client starts
    Check(keeper.Apply() && File.ReadAllText(roblox) == mine, "apply puts the saved settings back");
    Check(File.ReadAllText(kept + ".original") == defaults, "the file Roblox had is backed up before the first replace");

    File.WriteAllText(roblox, "<roblox>changed later</roblox>");
    keeper.Apply();
    Check(File.ReadAllText(kept + ".original") == defaults, "the backup is kept once and never overwritten");

    File.WriteAllText(kept, "this is not xml");
    File.WriteAllText(roblox, defaults);
    Check(!keeper.Apply() && File.ReadAllText(roblox) == defaults, "a damaged saved copy is never applied");

    keeper.Clear();
    Check(!keeper.HasSaved && !keeper.Apply(), "clear stops applying");
    Directory.Delete(dir, true);
}

// Motion switch: on = the value animates, off = it jumps with no animation; old settings files default to motion on.
{
    string? problem = null;
    var th = new Thread(() =>
    {
        try
        {
            var el = new System.Windows.Controls.Border { Opacity = 1 };
            Animations.State.On = true;
            Animations.Animate(el, UIElement.OpacityProperty, 0, 300);
            if (!el.HasAnimatedProperties) problem = "with motion on the value should animate";
            Animations.State.On = false;
            Animations.Animate(el, UIElement.OpacityProperty, 0.25, 300);
            if (el.HasAnimatedProperties || el.Opacity != 0.25) problem = "with motion off the value should jump, with no animation left running";
        }
        catch (Exception e) { problem = e.Message; }
        finally { Animations.State.On = true; }
    });
    th.SetApartmentState(ApartmentState.STA);
    th.Start();
    th.Join();
    Check(problem is null, "motion switch: " + problem);
    Check(System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Multi\":true}")!.Animations, "settings saved before the switch existed keep motion on");
    Check(!System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Animations\":false}")!.Animations, "motion off is read back from settings");
}

// Multi-instance lock must be OWNED, not just created: another thread (standing in for a Roblox client) must not be able to take it.
{
    bool exists; try { using var probe = Mutex.OpenExisting("ROBLOX_singletonMutex"); exists = true; } catch (WaitHandleCannotBeOpenedException) { exists = false; }
    if (!exists) // skip when a real Roblox, RoDock or Account Manager holds it
    {
        Roblox.SetMultiInstance(true);
        bool clientCouldTake = true;
        var th = new Thread(() => { using var m = Mutex.OpenExisting("ROBLOX_singletonMutex"); try { clientCouldTake = m.WaitOne(0); } catch (AbandonedMutexException) { clientCouldTake = true; } });
        th.Start(); th.Join();
        Check(!clientCouldTake, "multi-instance lock is owned, so a Roblox client cannot take it");
        Roblox.SetMultiInstance(false);
        Check(!Mutex.TryOpenExisting("ROBLOX_singletonMutex", out _), "turning multi-instance off removes the lock");
    }
}

// The cookie may only ever go to Roblox over https (look-alike hosts and plain http are refused).
Check(Roblox.IsRoblox(new Uri("https://www.roblox.com/x")) && Roblox.IsRoblox(new Uri("https://roblox.com")) && Roblox.IsRoblox(new Uri("https://auth.roblox.com")), "roblox hosts allowed");
Check(!Roblox.IsRoblox(new Uri("https://evilroblox.com")) && !Roblox.IsRoblox(new Uri("https://roblox.com.evil.com")) &&
      !Roblox.IsRoblox(new Uri("http://www.roblox.com")) && !Roblox.IsRoblox(new Uri("https://evil.com/.roblox.com")), "look-alike hosts refused");

// The Discord token must never show up in text (logs, errors).
Check(!new DiscordConfig("SECRET-TOKEN", 42, 7).ToString().Contains("SECRET-TOKEN"), "DiscordConfig.ToString hides token");

// Discord token must be stored encrypted.
var dpath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
Vault.Write(dpath, new DiscordConfig("SECRET-TOKEN", 42, 7));
Check(!File.ReadAllText(dpath).Contains("SECRET-TOKEN"), "discord file must not contain plaintext token");
Check(Vault.Read<DiscordConfig>(dpath) == new DiscordConfig("SECRET-TOKEN", 42, 7), "discord config round-trip");
File.Delete(dpath);

Console.WriteLine("OK");

static void RunPreview(string outDir)
{
    Directory.CreateDirectory(outDir);
    var t = new Thread(() =>
    {
        Velopack.VelopackApp.Build().Run(); // the real app does this in Program.Main
        var app = new RoDock.App();
        app.InitializeComponent();
        foreach (var mode in new[] { "full", "simple", "empty", "ink", "midnight", "rose", "themes" })
        {
            var simple = mode == "simple";
            var custom = new Theme("Custom", "#102A43", "#1B3B5A", "#0B1F33", "#FF7A59", "#FFFFFF");
            var themeName = mode switch { "ink" => "Ink", "midnight" => "Midnight", "rose" => "Rose", "themes" => "Custom", _ => null };
            var dir = Path.Combine(Path.GetTempPath(), "rl-preview-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            var store = new Store(Path.Combine(dir, "accounts.dat"));
            if (mode != "empty")
            {
                store.Save([
                    new Account { Name = "builderman", Cookie = "x", UserId = 156, PlaceId = 4924922222 },
                    new Account { Name = "Shedletsky", Cookie = "x", UserId = 261, PlaceId = 4924922222, PrivateServerLink = "https://www.roblox.com/games/4924922222/Brookhaven?privateServerLinkCode=123" },
                    new Account { Name = "Roblox", Cookie = "x", UserId = 1, PlaceId = 920587237 },
                    new Account { Name = "alt_four", Cookie = "x", UserId = 2 },
                ]);
                // A week of made-up history so the charts have something to draw.
                var hist = new History(Path.Combine(dir, "history.json"));
                for (var h = 0; h < 24 * 7; h++)
                    foreach (var (id, hours) in new[] { (156L, 22), (261L, 12), (1L, 7), (2L, 3) })
                        if ((h * 7 + id) % 24 < hours) hist.Add(DateTime.UtcNow.AddHours(-h), id, 55, h % 13 == 0 ? 1 : 0);
                hist.Save();
            }
            File.WriteAllText(Path.Combine(dir, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new Settings(simple, false, null, themeName, mode == "themes" ? [custom] : null)));

            var w = new MainWindow(store, Path.Combine(dir, "settings.json")) { Left = -5000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            w.Show();
            var a = w.Accounts;
            if (a.Count > 0)
            {
                a[0].Running = true; a[0].Report("In game", Kind.Good);
                a[1].Running = true; a[1].Report("Left the game. Retrying in 24s", Kind.Warn);
                a[2].Running = true; a[2].Report("Joining...", Kind.Working);
                a[3].Report("Set a Place ID first", Kind.Bad);
            }
            if (mode == "themes") w.ThemeToggle.IsChecked = true;
            w.UpdateText.Text = "Update to 0.2.0";
            w.UpdateToast.Visibility = Visibility.Visible;
            Wait(6000);
            var rtb = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(w);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(Path.Combine(outDir, mode + ".png"))) enc.Save(fs);
            w.Close();
        }

        // The colour picker on its own (a Popup is a separate window and is not part of the window render).
        var picker = new ColorPicker();
        picker.SetColor(Color.FromRgb(0xFF, 0x7A, 0x59));
        var host = new System.Windows.Controls.Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(20), Background = (Brush)app.FindResource("Deep"), Child = picker };
        host.Measure(new Size(400, 400));
        host.Arrange(new Rect(host.DesiredSize));
        var prtb = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        prtb.Render(host);
        var penc = new PngBitmapEncoder();
        penc.Frames.Add(BitmapFrame.Create(prtb));
        using (var pfs = File.Create(Path.Combine(outDir, "picker.png"))) penc.Save(pfs);
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
}

// Opens the login window off-screen, checks it loads Roblox's login page, then closes it. Logs in as nobody.
static void RunLoginCheck()
{
    var t = new Thread(() =>
    {
        new RoDock.App().InitializeComponent();
        var w = new LoginWindow { Left = -5000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        Wait(10000);
        Console.WriteLine($"title: {w.PageTitle ?? "(not loaded)"}");
        Console.WriteLine($"error: {w.Error ?? "none"}");
        Console.WriteLine($"cookie captured: {w.Cookie != null}");
        w.Close();
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
}

// Drives the real window off-screen and reads the animated values: entrances start below 1 and settle at 1, press shrinks to 0.97 and comes back,
// the title bar buttons stay put, the switch knob slides and stretches, a status turning good pulses its dot, and with Motion off nothing moves.
static void RunMotionCheck()
{
    var t = new Thread(() =>
    {
        Velopack.VelopackApp.Build().Run(); // the real app does this in Program.Main
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata(60)); // as in Program.Main
        Animations.Hook();
        var app = new RoDock.App();
        app.InitializeComponent();
        var dir = Path.Combine(Path.GetTempPath(), "rl-motion-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var store = new Store(Path.Combine(dir, "accounts.dat"));
        store.Save([new Account { Name = "a", Cookie = "x", UserId = 1, PlaceId = 1 }]);
        File.WriteAllText(Path.Combine(dir, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new Settings(false, false, null, "Ink", null)));
        var w = new MainWindow(store, Path.Combine(dir, "settings.json")) { Left = -5000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        void Fail(string what) { Console.WriteLine("FAIL: " + what); Environment.Exit(1); }
        void Is(bool ok, string what) { if (!ok) Fail(what); }
        static IEnumerable<DependencyObject> Below(DependencyObject d)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var c = VisualTreeHelper.GetChild(d, i); yield return c; foreach (var x in Below(c)) yield return x; }
        }
        static void Mouse(UIElement e, RoutedEvent ev) => e.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = ev, Source = e });

        // Entrance: mid-way it is partly faded, afterwards it is back at its own values.
        // (Called directly after a warm-up: at start-up and on a view switch the layout stalls the UI thread for longer than the animation lasts, so it would just land on the end.)
        Wait(3000);
        Animations.Enter(w.Stats);
        Wait(300);
        var seenFade = false; // sampled on each frame: a timer is starved by rendering while the fade runs
        EventHandler sample = (_, _) => seenFade |= w.DashView.Opacity < 1;
        CompositionTarget.Rendering += sample;
        Animations.Enter(w.DashView);
        Wait(400);
        CompositionTarget.Rendering -= sample;
        Is(seenFade, "dashboard should fade in");
        w.ListRadio.IsChecked = true; w.CardsRadio.IsChecked = true; // switching views must not leave anything stuck
        Wait(1200);
        Is(w.DashView.Opacity == 1, "dashboard should end fully visible");
        Is(w.Cards.Items.Count > 0 && Below(w.Cards).OfType<FrameworkElement>().All(e => e.Opacity == 1), "dock tiles should end fully visible");

        // Press: 0.97 while held, back to 1 after; the caption buttons are left alone.
        var btn = w.ThemeToggle;
        Mouse(btn, UIElement.PreviewMouseLeftButtonDownEvent);
        Wait(250);
        var scale = (ScaleTransform)((TransformGroup)btn.RenderTransform).Children[0];
        Is(Math.Abs(scale.ScaleX - .97) < .005, $"held button should be at 0.97, was {scale.ScaleX}");
        Mouse(btn, UIElement.PreviewMouseLeftButtonUpEvent);
        Wait(400);
        Is(scale.ScaleX == 1, $"released button should be back at 1, was {scale.ScaleX}");
        Mouse(w.MaxButton, UIElement.PreviewMouseLeftButtonDownEvent);
        Wait(150);
        Is(w.MaxButton.RenderTransform is not TransformGroup, "caption buttons should not scale");
        Mouse(w.MaxButton, UIElement.PreviewMouseLeftButtonUpEvent);

        // Pulse: a status turning good grows its dot.
        w.Accounts[0].Report("In game", Kind.Good);
        Wait(150);
        Is(Below(w.Cards).OfType<System.Windows.Shapes.Ellipse>().Any(e => e.RenderTransform is TransformGroup { Children: [ScaleTransform { ScaleX: > 1.2 }, _] }), "a dot should pulse when the status turns good");

        // Switch knob: starts on the old side, stretched mid-way, rests at 0.
        var host = new Window { Left = -5000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        var cb = new System.Windows.Controls.CheckBox { Style = (Style)app.FindResource("SwitchBox"), Margin = new Thickness(20) };
        host.Content = cb;
        host.Show();
        Wait(150);
        var slide = (TranslateTransform)cb.Template.FindName("slide", cb);
        var stretch = (ScaleTransform)cb.Template.FindName("stretch", cb);
        cb.IsChecked = true;
        Wait(100);
        Is(slide.X < -1 && slide.X > -17 && stretch.ScaleX > 1.03, $"knob should be mid-slide and stretched, was x={slide.X} stretch={stretch.ScaleX}");
        Wait(400);
        Is(slide.X == 0 && stretch.ScaleX == 1, "knob should rest at 0");

        // Motion off: no knob slide, no entrance, nothing left half-way.
        Animations.State.On = false;
        cb.IsChecked = false;
        Wait(40);
        Is(slide.X == 0, "no knob slide with motion off");
        var b = new System.Windows.Controls.Border();
        Animations.Enter(b);
        Is(b.Opacity == 1 && b.RenderTransform is not TransformGroup, "no entrance with motion off");
        Mouse(btn, UIElement.PreviewMouseLeftButtonDownEvent);
        Wait(150);
        Is(scale.ScaleX == 1, "no press scale with motion off");

        host.Close();
        w.Close();
        Console.WriteLine("motion OK");
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
}

// How many frames does an entrance really draw, per element? (60 fps over 200 ms is about 12.) Prints; checks nothing.
static void RunFpsProbe()
{
    var t = new Thread(() =>
    {
        Velopack.VelopackApp.Build().Run();
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata(60));
        var app = new RoDock.App();
        app.InitializeComponent();
        var dir = Path.Combine(Path.GetTempPath(), "rl-fps-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var store = new Store(Path.Combine(dir, "accounts.dat"));
        store.Save([
            new Account { Name = "builderman", Cookie = "x", UserId = 156, PlaceId = 4924922222 },
            new Account { Name = "Shedletsky", Cookie = "x", UserId = 261, PlaceId = 4924922222 },
            new Account { Name = "Roblox", Cookie = "x", UserId = 1, PlaceId = 920587237 },
        ]);
        File.WriteAllText(Path.Combine(dir, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new Settings(false, false, null, "Ink", null)));
        var w = new MainWindow(store, Path.Combine(dir, "settings.json")) { Left = -5000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        Wait(4000);
        foreach (var (name, el) in new (string, UIElement)[] { ("warm-up", w.Stats), ("whole dashboard", w.DashView), ("hero card", w.Hero), ("dock tiles", w.Cards), ("stat row", w.Stats), ("activity list", w.Feed) })
        {
            var steps = new HashSet<double>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long done = 0;
            EventHandler count = (_, _) => { if (el.Opacity < 1) { steps.Add(el.Opacity); done = clock.ElapsedMilliseconds; } };
            CompositionTarget.Rendering += count;
            Animations.Enter(el);
            Wait(500);
            CompositionTarget.Rendering -= count;
            Console.WriteLine($"{name,-16} {steps.Count,3} frames over {done} ms");
        }
        w.Close();
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
}

static void Wait(int ms)
{
    var frame = new DispatcherFrame();
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
    timer.Start();
    Dispatcher.PushFrame(frame);
}

// Launches Roblox clients with a FAKE login ticket (they cannot log in) to see how Roblox reacts to RoDock's multi-instance lock and
// how its processes behave. Prints a timeline of every RobloxPlayerBeta process; always kills everything it started.
//   phase A: lock ON, 2 launches   phase B: lock OFF, 2 launches   phase C: lock ON, close client 1's main process, launch again
static void RunMultiCheck()
{
    static System.Diagnostics.Process[] Rb() => System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta");
    if (Rb().Length > 0) { Console.WriteLine("ABORT: close all Roblox windows first"); return; }

    var start = DateTime.Now;
    string T() => $"{(DateTime.Now - start).TotalSeconds,5:0.0}s";

    static string Describe(System.Diagnostics.Process p)
    {
        try { return $"pid {p.Id}{(p.MainWindowHandle != 0 ? " [window]" : " [no window]")}"; } catch { return $"pid {p.Id} [?]"; }
    }

    // Watches for `seconds`, printing each process the moment it appears or disappears. Returns every pid ever seen.
    HashSet<int> Watch(int seconds, HashSet<int> alreadyKnown)
    {
        var seen = new HashSet<int>(alreadyKnown);
        var alive = Rb().Select(p => p.Id).ToHashSet();
        for (var i = 0; i < seconds * 4; i++)
        {
            Thread.Sleep(250);
            var now = Rb();
            var nowIds = now.Select(p => p.Id).ToHashSet();
            foreach (var p in now.Where(p => !alive.Contains(p.Id))) { Console.WriteLine($"  {T()} + appeared {Describe(p)}"); seen.Add(p.Id); }
            foreach (var id in alive.Where(id => !nowIds.Contains(id))) Console.WriteLine($"  {T()} - exited   pid {id}");
            alive = nowIds;
        }
        Console.WriteLine($"  {T()} alive now: {alive.Count} -> [{string.Join(", ", Rb().Select(Describe))}]");
        return seen;
    }

    void Launch(string label)
    {
        Console.WriteLine($"  {T()} launching {label}");
        var uri = Roblox.BuildLaunchUri("FAKE-TICKET", 4924922222, null, null, Random.Shared.Next(100000, 999999).ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    }

    void KillAll()
    {
        foreach (var p in Rb()) try { p.Kill(true); p.WaitForExit(5000); } catch { }
        Thread.Sleep(1500);
        Console.WriteLine($"  cleanup: {Rb().Length} Roblox process(es) left");
    }

    foreach (var (name, lockOn) in new[] { ("A: lock ON, 2 launches", true), ("B: lock OFF, 2 launches", false) })
    {
        Console.WriteLine($"== {name}");
        try { Roblox.SetMultiInstance(lockOn); } catch (Exception ex) { Console.WriteLine($"  lock: {ex.Message}"); continue; }
        Launch("client 1"); var seen = Watch(9, []);
        Launch("client 2"); Watch(9, seen);
        Roblox.SetMultiInstance(false);
        KillAll();
    }

    Console.WriteLine("== C: lock ON, launch 1, kill ONLY its main process (tray child stays), launch again");
    try { Roblox.SetMultiInstance(true); } catch (Exception ex) { Console.WriteLine($"  lock: {ex.Message}"); return; }
    Launch("client 1"); var s1 = Watch(9, []);
    var main = Rb().FirstOrDefault(p => p.MainWindowHandle != 0);
    if (main != null) { Console.WriteLine($"  {T()} killing main {main.Id}"); main.Kill(); main.WaitForExit(5000); }
    Watch(3, s1);
    Launch("client 2"); Watch(9, s1);
    Roblox.SetMultiInstance(false);
    KillAll();
}

// Launches a fake-ticket client 3 times in a row and closes it with RoDock's real close routine; nothing may be left behind.
static void RunCloseCheck()
{
    static System.Diagnostics.Process[] Rb() => System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta");
    if (Rb().Length > 0) { Console.WriteLine("ABORT: close all Roblox windows first"); return; }

    var ok = true;
    for (var round = 1; round <= 3; round++)
    {
        var uri = Roblox.BuildLaunchUri("FAKE-TICKET", 4924922222, null, null, Random.Shared.Next(100000, 999999).ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        Thread.Sleep(9000);
        var account = new Account { Pids = Rb().Select(p => p.Id).ToArray() };
        var started = account.Pids.Length;
        Console.WriteLine($"round {round}: client log found for the new process: {account.Pids.Select(ClientLog.For).Any(l => l is not null)}");
        Supervisor.CloseClients(account).Wait();
        Thread.Sleep(2000);
        var left = Rb().Length;
        Console.WriteLine($"round {round}: started {started} process(es), left after RoDock close: {left}");
        if (left > 0) { ok = false; foreach (var p in Rb()) try { p.Kill(true); } catch { } Thread.Sleep(1000); }
    }
    Console.WriteLine(ok ? "close-check: PASS" : "close-check: FAIL (leftovers were killed by the harness)");
}

// Needs network and an installed Roblox, so it is a manual mode, not part of the default self-checks.
// An expired/invalid cookie must stop the account with a clear message, launch nothing, and not keep retrying.
static async Task RunCookieCheck()
{
    var a = new Account { Name = "bogus", Cookie = "this-is-not-a-real-cookie", UserId = 1, PlaceId = 4924922222 };
    var sup = new Supervisor();
    var t0 = DateTime.Now;
    sup.Start(a);
    while (a.Running && DateTime.Now - t0 < TimeSpan.FromSeconds(60)) await Task.Delay(500);
    var roblox = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Length;
    Console.WriteLine($"after {(DateTime.Now - t0).TotalSeconds:0.0}s: running={a.Running} kind={a.StatusKind} status='{a.Status}' robloxProcesses={roblox}");
    var ok = !a.Running && a.StatusKind == Kind.Bad && a.Status.Contains("Cookie expired") && roblox == 0;
    Console.WriteLine(ok ? "cookie-check: PASS" : "cookie-check: FAIL");
}

// Needs Roblox installed; briefly starts one fake-ticket client. Answers: when a client starts, does Roblox keep the settings RoDock put
// in place, or reset them? Uses the REAL settings file, so it backs it up first and always restores it.
static void RunSettingsCheck()
{
    var real = RobloxSettings.Default;
    var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml");
    var savedProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoDock", "roblox-settings.xml");
    if (real.HasSaved) { Console.WriteLine("ABORT: you already have a saved profile; this check would replace it."); return; }

    var original = File.ReadAllBytes(file);
    static string Value(string path, string name) =>
        System.Xml.Linq.XDocument.Load(path).Descendants().First(e => (string?)e.Attribute("name") == name).Value;
    Console.WriteLine($"before: GraphicsQualityLevel={Value(file, "GraphicsQualityLevel")} FramerateCap={Value(file, "FramerateCap")}");
    var before = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Select(p => p.Id).ToHashSet();
    try
    {
        var doc = System.Xml.Linq.XDocument.Load(file);
        foreach (var (name, v) in new[] { ("GraphicsQualityLevel", "5"), ("FramerateCap", "60") })
            doc.Descendants().First(e => (string?)e.Attribute("name") == name).Value = v;
        Directory.CreateDirectory(Path.GetDirectoryName(savedProfile)!);
        doc.Save(savedProfile);
        real.Apply();
        Console.WriteLine($"applied: GraphicsQualityLevel={Value(file, "GraphicsQualityLevel")} FramerateCap={Value(file, "FramerateCap")}");

        var uri = Roblox.BuildLaunchUri("FAKE-TICKET", 4924922222, null, null, Random.Shared.Next(100000, 999999).ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        Thread.Sleep(12000);
        Console.WriteLine($"after a client started: GraphicsQualityLevel={Value(file, "GraphicsQualityLevel")} FramerateCap={Value(file, "FramerateCap")}  (file written {File.GetLastWriteTime(file):HH:mm:ss}, now {DateTime.Now:HH:mm:ss})");
    }
    finally
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Where(p => !before.Contains(p.Id)))
            try { p.Kill(true); p.WaitForExit(5000); } catch { }
        Thread.Sleep(1500);
        File.WriteAllBytes(file, original);
        real.Clear();
        File.Delete(savedProfile + ".original");
        Console.WriteLine($"restored your original file; saved test profile removed. GraphicsQualityLevel={Value(file, "GraphicsQualityLevel")} FramerateCap={Value(file, "FramerateCap")}");
    }
}

// Measures what typical UI animations cost in CPU (percent of ONE core), with software or GPU rendering:
//   dotnet run --project RoDock.Tests -- anim-check sw|hw
static void RunAnimCheck(bool software)
{
    var t = new Thread(() =>
    {
        if (Environment.GetEnvironmentVariable("RODOCK_CAP") == "1") // same global cap RoDock applies in Program.Main
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata(30));
        Console.WriteLine($"default animation frame rate in this run: {System.Windows.Media.Animation.Timeline.GetDesiredFrameRate(new System.Windows.Media.Animation.DoubleAnimation())?.ToString() ?? "60 (unset)"}");
        var app = new System.Windows.Application();
        if (software) System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var root = new System.Windows.Controls.StackPanel { Background = System.Windows.Media.Brushes.Black, Margin = new Thickness(20) };
        var dots = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var dotList = Enumerable.Range(0, 12).Select(_ => new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Margin = new Thickness(4), Fill = System.Windows.Media.Brushes.LimeGreen }).ToList();
        dotList.ForEach(d => dots.Children.Add(d));
        var spinner = new System.Windows.Controls.Border { Width = 40, Height = 40, BorderThickness = new Thickness(5), BorderBrush = System.Windows.Media.Brushes.SkyBlue, CornerRadius = new CornerRadius(20), RenderTransformOrigin = new Point(.5, .5), RenderTransform = new System.Windows.Media.RotateTransform(), Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left };
        var shimmer = new System.Windows.Shapes.Rectangle { Width = 120, Height = 8, Fill = System.Windows.Media.Brushes.White, RenderTransform = new System.Windows.Media.TranslateTransform() };
        var bar = new System.Windows.Controls.Border { Width = 400, Height = 8, Background = System.Windows.Media.Brushes.DimGray, ClipToBounds = true, Child = shimmer, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
        var big = new System.Windows.Controls.Border { Width = 900, Height = 300, CornerRadius = new CornerRadius(24), Background = System.Windows.Media.Brushes.SteelBlue };
        root.Children.Add(dots); root.Children.Add(spinner); root.Children.Add(bar); root.Children.Add(big);
        var win = new Window { Content = root, Width = 1000, Height = 640, Left = -3000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Background = System.Windows.Media.Brushes.Black };
        win.Show();

        System.Windows.Media.Animation.DoubleAnimation Anim(double from, double to, double ms, int? fps)
        {
            var a = new System.Windows.Media.Animation.DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            if (fps is { } f) System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(a, f);
            return a;
        }
        void Start(bool small, bool large, int? fps)
        {
            if (small)
            {
                foreach (var d in dotList) d.BeginAnimation(UIElement.OpacityProperty, Anim(1, .35, 900, fps));
                var spin = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
                if (fps is { } f) System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(spin, f);
                ((System.Windows.Media.RotateTransform)spinner.RenderTransform).BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, spin);
                ((System.Windows.Media.TranslateTransform)shimmer.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, Anim(-120, 400, 1500, fps));
            }
            if (large) big.BeginAnimation(UIElement.OpacityProperty, Anim(1, .6, 2000, fps));
        }
        void Stop()
        {
            foreach (var d in dotList) d.BeginAnimation(UIElement.OpacityProperty, null);
            ((System.Windows.Media.RotateTransform)spinner.RenderTransform).BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            ((System.Windows.Media.TranslateTransform)shimmer.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            big.BeginAnimation(UIElement.OpacityProperty, null);
        }

        var phases = new (string Name, Action Begin)[]
        {
            ("nothing animating", () => { }),
            ("12 pulsing dots + spinner + shimmer, 60 fps", () => Start(true, false, null)),
            ("same, capped at 30 fps", () => { Stop(); Start(true, false, 30); }),
            ("same + one big 900x300 panel fading, 60 fps", () => { Stop(); Start(true, true, null); }),
            ("only the big fading panel, 30 fps", () => { Stop(); Start(false, true, 30); }),
        };
        Wait(2500); // let the window settle
        var me = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine($"[{(software ? "software" : "GPU")} rendering]");
        Start(true, true, null); Wait(3000); Stop(); Wait(1500); // warm-up, so one-time JIT/start-up cost isn't counted
        foreach (var (name, begin) in phases)
        {
            begin(); Wait(1500);
            me.Refresh(); var cpu0 = me.TotalProcessorTime; var sw = System.Diagnostics.Stopwatch.StartNew();
            Wait(6000);
            me.Refresh(); var pct = (me.TotalProcessorTime - cpu0).TotalSeconds / sw.Elapsed.TotalSeconds * 100;
            Console.WriteLine($"  {name,-48} {pct,5:0.0}% of one core");
        }
        win.Close();
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
}
