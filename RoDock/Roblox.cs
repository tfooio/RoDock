using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoDock;

/// The cookie was rejected by Roblox: stop retrying, the user must re-add the account.
public class AuthException() : Exception("Cookie expired. Re-add the account.");

public static class Roblox
{
    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
    static readonly ConcurrentDictionary<string, string> Csrf = new();
    static readonly ConcurrentDictionary<long, (string Name, string? Icon)> Games = new();
    static Mutex? _multi;

    static HttpRequestMessage Req(HttpMethod method, string url, string cookie)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.TryAddWithoutValidation("Cookie", ".ROBLOSECURITY=" + cookie);
        r.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
        return r;
    }

    /// Authenticated request. Handles Roblox's CSRF dance (403 + x-csrf-token, then resend) and maps 401 to AuthException.
    static async Task<HttpResponseMessage> Send(HttpMethod method, string url, string cookie, string? json = null)
    {
        async Task<HttpResponseMessage> Once()
        {
            var r = Req(method, url, cookie);
            if (method == HttpMethod.Post) r.Content = new StringContent(json ?? "", Encoding.UTF8, "application/json");
            if (Csrf.TryGetValue(cookie, out var t)) r.Headers.TryAddWithoutValidation("x-csrf-token", t);
            return await Http.SendAsync(r);
        }

        var res = await Once();
        if (res.StatusCode == System.Net.HttpStatusCode.Forbidden && res.Headers.TryGetValues("x-csrf-token", out var fresh))
        {
            Csrf[cookie] = fresh.First();
            res.Dispose();
            res = await Once();
        }
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) { res.Dispose(); throw new AuthException(); }
        return res;
    }

    /// Id and name of the account the cookie belongs to; throws AuthException if the cookie is bad.
    public static async Task<(long Id, string Name)> GetUser(string cookie)
    {
        using var res = await Send(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", cookie);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("id").GetInt64(), doc.RootElement.GetProperty("name").GetString()!);
    }

    /// True/false = whether the account is currently in a game. Presence can lag a little behind reality.
    public static async Task<bool> IsInGame(string cookie, long userId)
    {
        using var res = await Send(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users", cookie, $"{{\"userIds\":[{userId}]}}");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("userPresences")[0].GetProperty("userPresenceType").GetInt32() == 2;
    }

    public static async Task<bool> IsOnline()
    {
        try { using var _ = await Http.GetAsync("https://www.roblox.com", HttpCompletionOption.ResponseHeadersRead); return true; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    static async Task<string> GetAuthTicket(string cookie)
    {
        using var res = await Send(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket", cookie);
        if (!res.Headers.TryGetValues("rbx-authentication-ticket", out var ticket))
            throw new InvalidOperationException($"Roblox gave no login ticket (HTTP {(int)res.StatusCode}).");
        return ticket.First();
    }

    static string? LinkCode(string? link) => link is null ? null : Regex.Match(link, @"privateServerLinkCode=([^&\s]+)") is { Success: true } m ? m.Groups[1].Value : null;

    // The access code only exists inside the game page's HTML, so this breaks if Roblox redesigns that page.
    static async Task<string> GetAccessCode(string cookie, long placeId, string linkCode)
    {
        using var res = await Send(HttpMethod.Get, $"https://www.roblox.com/games/{placeId}?privateServerLinkCode={linkCode}", cookie);
        var m = Regex.Match(await res.Content.ReadAsStringAsync(), @"joinPrivateGame\(\d+,\s*'([\w-]+)'");
        return m.Success ? m.Groups[1].Value
            : throw new InvalidOperationException("Could not read the private server access code (bad link, or Roblox changed the page).");
    }

    public static string BuildLaunchUri(string ticket, long placeId, string? accessCode, string? linkCode, string trackerId, long launchTimeMs)
    {
        var request = accessCode is null
            ? $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={trackerId}&placeId={placeId}&isPlayTogetherGame=false"
            : $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId={placeId}&accessCode={accessCode}&linkCode={linkCode}";
        return $"roblox-player:1+launchmode:play+gameinfo:{ticket}+launchtime:{launchTimeMs}+placelauncherurl:{Uri.EscapeDataString(request)}" +
               $"+browsertrackerid:{trackerId}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
    }

    static HashSet<int> RobloxPids() => Process.GetProcessesByName("RobloxPlayerBeta").Select(p => p.Id).ToHashSet();

    /// Starts Roblox for the account and returns the new client's process ids.
    // shortcut: new clients are found by diffing process ids, so a Roblox started by another app in the same few seconds could be mistaken for ours.
    public static async Task<int[]> Launch(string cookie, long placeId, string? privateLink)
    {
        string? access = null, code = LinkCode(privateLink);
        if (!string.IsNullOrWhiteSpace(privateLink))
        {
            if (code is null) throw new ArgumentException("Private server link must contain privateServerLinkCode=...");
            access = await GetAccessCode(cookie, placeId, code);
        }

        var ticket = await GetAuthTicket(cookie);
        var tracker = $"{Random.Shared.Next(100000, 175000)}{Random.Shared.Next(100000, 900000)}";
        var uri = BuildLaunchUri(ticket, placeId, access, code, tracker, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var before = RobloxPids();
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            if (RobloxPids().Except(before).ToArray() is { Length: > 0 }) { await Task.Delay(2000); return RobloxPids().Except(before).ToArray(); }
        }
        throw new InvalidOperationException("Roblox did not start.");
    }

    // --- pictures and names (public data, no login needed) ---

    static async Task<string?> ImageUrl(string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            return doc.RootElement.GetProperty("data")[0].GetProperty("imageUrl").GetString();
        }
        catch { return null; }
    }

    public static Task<string?> AvatarUrl(long userId) =>
        ImageUrl($"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false");

    public static async Task<(string Name, string? Icon)?> GameInfo(long placeId)
    {
        if (Games.TryGetValue(placeId, out var cached)) return cached;
        try
        {
            using var u = JsonDocument.Parse(await Http.GetStringAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe"));
            var universe = u.RootElement.GetProperty("universeId").GetInt64();
            using var g = JsonDocument.Parse(await Http.GetStringAsync($"https://games.roblox.com/v1/games?universeIds={universe}"));
            var name = g.RootElement.GetProperty("data")[0].GetProperty("name").GetString()!;
            var icon = await ImageUrl($"https://thumbnails.roblox.com/v1/games/icons?universeIds={universe}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false");
            return Games[placeId] = (name, icon);
        }
        catch { return null; }
    }

    /// Roblox quits at startup if the "ROBLOX_singletonMutex" mutex already exists, so we create it first.
    public static void SetMultiInstance(bool on)
    {
        if (on && _multi == null)
        {
            _multi = new Mutex(false, "ROBLOX_singletonMutex", out var created);
            if (!created) { _multi.Dispose(); _multi = null; throw new InvalidOperationException("Another app (or an open Roblox) already holds the multi-instance lock."); }
        }
        else if (!on) { _multi?.Dispose(); _multi = null; }
    }
}
