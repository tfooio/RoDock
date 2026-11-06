using System.Diagnostics;

namespace RoDock;

/// Why an account is not in its game, and what to do about it.
public record Failure(string Reason, Act Act);

/// Keeps each started account in its game: launches it, watches it, and closes + relaunches it when it drops.
public class Supervisor
{
    static readonly int[] BackoffSeconds = [10, 30, 60, 120, 300];
    const int MaxFailures = 5;
    const int SlowSeconds = 120; // minimum wait after errors Roblox lists as "wait longer" (see RobloxErrors)
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
                if (a.PlaceId is not > 0 && string.IsNullOrWhiteSpace(a.PrivateServerLink)) { Finish(a, "Set a Place ID first", Kind.Bad); return; }
                if (!await Roblox.IsOnline()) { a.Report("Waiting for network...", Kind.Warn); await Task.Delay(15000, ct); continue; }

                Failure failure;
                try
                {
                    if (await JoinGame(a, ct) is { } joinFailure) failure = joinFailure;
                    else
                    {
                        var upSince = DateTime.UtcNow;
                        failure = await Watch(a, ct);
                        if (DateTime.UtcNow - upSince > StableAfter) failures = 0;
                    }
                }
                catch (Exception e) when (e is AuthException or RobloxNotInstalledException) { Finish(a, e.Message, Kind.Bad); return; } // retrying can't fix these
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { failure = new Failure(e.Message, Act.Retry); }

                // Roblox said retrying can't help (ban, moderation, link revoked...): close the error screen and tell the user.
                if (failure.Act == Act.Stop) { await CloseClients(a); Finish(a, $"Stopped: {failure.Reason}. Needs your attention.", Kind.Bad); return; }
                if (++failures >= MaxFailures) { Finish(a, $"Paused after {MaxFailures} failures: {failure.Reason}", Kind.Bad); return; }
                var wait = Math.Max(BackoffSeconds[Math.Min(failures - 1, BackoffSeconds.Length - 1)], failure.Act == Act.Slow ? SlowSeconds : 0);
                for (var s = wait; s > 0; s--)
                {
                    a.Report($"{failure.Reason}. Retrying in {s}s", Kind.Warn);
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException) { } // Stop() already updated the status
    }

    /// Closes the old client, launches a new one, and waits until Roblox reports the account in a game.
    /// Returns null once in game, or why it failed. An error in the client's own log fails it within seconds (not after the 90 s timeout).
    async Task<Failure?> JoinGame(Account a, CancellationToken ct)
    {
        await _launchGate.WaitAsync(ct);
        try
        {
            a.Report("Closing old client...", Kind.Working);
            await CloseClients(a);
            a.Report("Launching...", Kind.Working);
            var (pids, log) = await Roblox.Launch(a.Cookie, a.PlaceId ?? 0, a.PrivateServerLink, a.UserId);
            a.Pids = pids;
            a.Trace($"client pids {string.Join(",", a.Pids.Select(p => $"{p} {Build(p)}"))}{(log is not null ? " (took the join)" : "")}");
            a.Log = log ?? a.Pids.Select(ClientLog.For).FirstOrDefault(l => l is not null);

            a.Report("Joining...", Kind.Working);
            for (var i = 0; i < 18; i++) // ~90 s
            {
                await Task.Delay(5000, ct);
                if (a.Pids.Length > 0 && a.Pids.All(IsDead)) return new Failure("Client closed while joining", Act.Retry); // no pids = joining in an existing client
                if (FirstFailure(a.Log?.Poll() ?? []) is { } failure) return failure;
                if (await TryInGame(a) == true) { a.Report("In game", Kind.Good); return null; }
            }
            return new Failure("Did not get into the game in time", Act.Retry);
        }
        finally { _launchGate.Release(); }
    }

    /// Returns why the account dropped. Process exit is noticed within 5 s; an error in the client's log within about 30 s
    /// (the wait lets a teleport, which also logs a disconnect, rejoin first); leaving the game needs 3 presence misses in a row (~45 s).
    async Task<Failure> Watch(Account a, CancellationToken ct)
    {
        int tick = 0, misses = 0;
        Failure? pending = null;
        var pendingAt = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(5000, ct);
            if (a.Pids.Length > 0 && a.Pids.All(IsDead)) return new Failure("Client closed", Act.Retry);

            foreach (var e in a.Log?.Poll() ?? [])
                if (e.Kind == LogKind.Join) pending = null; // joined again: it was a teleport
                else if (pending is null || e.Code is not null)
                {
                    pending = FirstFailure([e]);
                    pendingAt = DateTime.UtcNow;
                    a.Report($"{pending!.Reason}, waiting to see if it rejoins...", Kind.Warn);
                }
            if (pending is not null && DateTime.UtcNow - pendingAt > TimeSpan.FromSeconds(25)) return pending;

            if (++tick % 3 != 0) continue;
            switch (await TryInGame(a))
            {
                case true: misses = 0; if (pending is null) a.Report("In game", Kind.Good); break;
                case false when ++misses >= 3: return pending ?? new Failure("Left the game", Act.Retry);
                case false: a.Report($"Not in game ({misses}/3)...", Kind.Warn); break;
            }
        }
    }

    /// The first failure in the log events, described with Roblox's own error name and the action for it; null if there is none.
    internal static Failure? FirstFailure(IEnumerable<LogEvent> events)
    {
        var failures = events.Where(e => e.Kind == LogKind.Failure).ToList();
        if (failures.Count == 0) return null;
        var (text, act) = RobloxErrors.Describe(failures.Select(f => f.Code).FirstOrDefault(c => c is not null));
        return new Failure(text, act);
    }

    /// null = couldn't tell (network hiccup); a bad cookie still throws AuthException.
    static async Task<bool?> TryInGame(Account a)
    {
        try { return await Roblox.IsInGame(a.Cookie, a.UserId); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    /// Which Roblox build a client runs, for the trace ("version-..."); "?" if it can't be read.
    static string Build(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return Path.GetFileName(Path.GetDirectoryName(p.MainModule?.FileName)) ?? "?"; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return "?"; }
    }

    static bool IsDead(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.HasExited || p.ProcessName != "RobloxPlayerBeta"; }
        catch (ArgumentException) { return true; }
    }

    /// Force-closes the clients we started and waits until they are gone. Not a polite close: that makes Roblox leave a hidden
    /// "--launch-to-tray" copy, and killing that copy left a dead window handle that crashed every later launch (SingleInstanceGuard).
    /// shortcut: the server sees the client vanish instead of leaving, so an immediate rejoin could be refused for a moment; the retry loop covers it.
    internal static Task CloseClients(Account a)
    {
        var pids = a.Pids;
        if (pids.Length > 0) a.Trace($"closing pids {string.Join(",", pids)}");
        a.Pids = [];
        a.Log = null;
        return Task.Run(() =>
        {
            foreach (var pid in pids)
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.ProcessName != "RobloxPlayerBeta") continue;
                    p.Kill(true);
                    p.WaitForExit(5000);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { } // already gone, or not ours to touch
        });
    }
}
