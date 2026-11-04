using System.Diagnostics;

namespace RoDock;

/// Keeps each started account in its game: launches it, watches it, and closes + relaunches it when it drops.
public class Supervisor
{
    static readonly int[] BackoffSeconds = [10, 30, 60, 120, 300];
    const int MaxFailures = 5;
    static readonly TimeSpan StableAfter = TimeSpan.FromMinutes(5);

    readonly SemaphoreSlim _launchGate = new(1); // one launch/join in flight at a time
    readonly Dictionary<Account, CancellationTokenSource> _running = [];

    public void Start(Account a)
    {
        if (_running.ContainsKey(a)) return;
        var cts = _running[a] = new CancellationTokenSource();
        a.Running = true;
        _ = Run(a, cts.Token);
    }

    /// The returned task completes once the old client is really gone.
    public Task Stop(Account a)
    {
        if (!_running.Remove(a, out var cts)) return Task.CompletedTask;
        cts.Cancel();
        a.Running = false;
        a.Report("Stopped", Kind.Idle);
        return CloseClients(a);
    }

    void Finish(Account a, string text, Kind kind)
    {
        _running.Remove(a);
        a.Running = false;
        a.Report(text, kind);
    }

    async Task Run(Account a, CancellationToken ct)
    {
        var failures = 0;
        try
        {
            while (true)
            {
                if (a.PlaceId is not > 0) { Finish(a, "Set a Place ID first", Kind.Bad); return; }
                if (!await Roblox.IsOnline()) { a.Report("Waiting for network...", Kind.Warn); await Task.Delay(15000, ct); continue; }

                string reason;
                try
                {
                    if (await JoinGame(a, ct))
                    {
                        var upSince = DateTime.UtcNow;
                        reason = await Watch(a, ct);
                        if (DateTime.UtcNow - upSince > StableAfter) failures = 0;
                    }
                    else reason = "Did not get into the game in time";
                }
                catch (AuthException e) { Finish(a, e.Message, Kind.Bad); return; }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { reason = e.Message; }

                if (++failures >= MaxFailures) { Finish(a, $"Paused after {MaxFailures} failures: {reason}", Kind.Bad); return; }
                for (var s = BackoffSeconds[Math.Min(failures - 1, BackoffSeconds.Length - 1)]; s > 0; s--)
                {
                    a.Report($"{reason}. Retrying in {s}s", Kind.Warn);
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException) { } // Stop() already updated the status
    }

    /// Closes the old client, launches a new one, and waits until Roblox reports the account in a game.
    async Task<bool> JoinGame(Account a, CancellationToken ct)
    {
        await _launchGate.WaitAsync(ct);
        try
        {
            a.Report("Closing old client...", Kind.Working);
            await CloseClients(a);
            a.Report("Launching...", Kind.Working);
            a.Pids = await Roblox.Launch(a.Cookie, a.PlaceId!.Value, a.PrivateServerLink);

            a.Report("Joining...", Kind.Working);
            for (var i = 0; i < 18; i++) // ~90 s
            {
                await Task.Delay(5000, ct);
                if (a.Pids.All(IsDead)) return false;
                if (await TryInGame(a) == true) { a.Report("In game", Kind.Good); return true; }
            }
            return false;
        }
        finally { _launchGate.Release(); }
    }

    /// Returns why the account dropped. Process exit is noticed within 5 s; leaving the game needs 3 presence misses in a row (~45 s).
    async Task<string> Watch(Account a, CancellationToken ct)
    {
        int tick = 0, misses = 0;
        while (true)
        {
            await Task.Delay(5000, ct);
            if (a.Pids.All(IsDead)) return "Client closed";
            if (++tick % 3 != 0) continue;
            switch (await TryInGame(a))
            {
                case true: misses = 0; a.Report("In game", Kind.Good); break;
                case false when ++misses >= 3: return "Left the game";
                case false: a.Report($"Not in game ({misses}/3)...", Kind.Warn); break;
            }
        }
    }

    /// null = couldn't tell (network hiccup); a bad cookie still throws AuthException.
    static async Task<bool?> TryInGame(Account a)
    {
        try { return await Roblox.IsInGame(a.Cookie, a.UserId); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    static bool IsDead(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.HasExited || p.ProcessName != "RobloxPlayerBeta"; }
        catch (ArgumentException) { return true; }
    }

    /// Asks the client to close (so Roblox leaves the server cleanly), force-kills it if it doesn't, and waits until it is gone.
    internal static Task CloseClients(Account a)
    {
        var pids = a.Pids;
        a.Pids = [];
        return Task.Run(() =>
        {
            foreach (var pid in pids)
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.ProcessName != "RobloxPlayerBeta") continue;
                    p.CloseMainWindow();
                    if (!p.WaitForExit(2000)) { p.Kill(true); p.WaitForExit(5000); }
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException) { } // already gone

            // After a normal close Roblox leaves a background "--launch-to-tray" copy behind; without this they pile up on every rejoin.
            Thread.Sleep(1500);
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
                try { if (pids.Contains(NativeProcess.ParentPid(p))) { p.Kill(true); p.WaitForExit(3000); } }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } // gone or not ours to touch
        });
    }
}
