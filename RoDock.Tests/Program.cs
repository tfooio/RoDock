// No test framework needed.
//   dotnet run --project RoDock.Tests                 -> self-checks (exit code 1 on failure)
//   dotnet run --project RoDock.Tests -- preview DIR  -> renders both UI modes to DIR/*.png (no Roblox, no accounts, no login)
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RoDock;

if (args is ["preview", var outDir]) { RunPreview(outDir); return; }
if (args is ["login-check"]) { RunLoginCheck(); return; }
if (args is ["multi-check"]) { RunMultiCheck(); return; }
if (args is ["close-check"]) { RunCloseCheck(); return; }
if (args is ["cookie-check"]) { await RunCookieCheck(); return; }

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
        foreach (var simple in new[] { false, true })
        {
            var dir = Path.Combine(Path.GetTempPath(), "rl-preview-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            var store = new Store(Path.Combine(dir, "accounts.dat"));
            store.Save([
                new Account { Name = "builderman", Cookie = "x", UserId = 156, PlaceId = 4924922222 },
                new Account { Name = "Shedletsky", Cookie = "x", UserId = 261, PlaceId = 4924922222, PrivateServerLink = "https://www.roblox.com/games/4924922222/Brookhaven?privateServerLinkCode=123" },
                new Account { Name = "Roblox", Cookie = "x", UserId = 1, PlaceId = 920587237 },
                new Account { Name = "alt_four", Cookie = "x", UserId = 2 },
            ]);
            File.WriteAllText(Path.Combine(dir, "settings.json"), $"{{\"Simple\":{simple.ToString().ToLower()},\"Multi\":false}}");

            var w = new MainWindow(store, Path.Combine(dir, "settings.json")) { Left = -5000, Top = 0, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            w.Show();
            var a = w.Accounts;
            a[0].Running = true; a[0].Report("In game", Kind.Good);
            a[1].Running = true; a[1].Report("Left the game. Retrying in 24s", Kind.Warn);
            a[2].Running = true; a[2].Report("Joining...", Kind.Working);
            a[3].Report("Set a Place ID first", Kind.Bad);
            w.UpdateText.Text = "RoDock 0.2.0 is available";
            w.UpdateToast.Visibility = Visibility.Visible;
            Wait(6000);

            var rtb = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(w);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(Path.Combine(outDir, simple ? "simple.png" : "full.png"))) enc.Save(fs);
            w.Close();
        }
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
