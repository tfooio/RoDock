namespace RoDock;

/// What the Discord slash commands do. No Discord types in here so it can be tested offline.
/// Must be called on the UI thread (it touches accounts the UI is bound to).
public class Commands(IList<Account> accounts, Supervisor supervisor)
{
    public static readonly string[] WithAccount = ["start", "stop", "rejoin"];
    public static readonly string[] WithoutAccount = ["status", "startall", "stopall"];

    static string Icon(Kind k) => k switch { Kind.Good => "🟢", Kind.Warn => "🟡", Kind.Bad => "🔴", Kind.Working => "🔵", _ => "⚪" };

    public async Task<string> Handle(string command, string? accountName = null)
    {
        switch (command)
        {
            case "status":
                return accounts.Count == 0 ? "No accounts yet." : string.Join("\n", accounts.Select(a => $"{Icon(a.StatusKind)} **{a.Name}**: {a.Status}"));
            case "startall":
                foreach (var a in accounts) supervisor.Start(a);
                return $"Starting {accounts.Count} account(s).";
            case "stopall":
                await Task.WhenAll(accounts.ToList().Select(supervisor.Stop));
                return "Stopped all accounts and closed their clients.";
        }

        var acc = accounts.FirstOrDefault(a => a.Name.Equals(accountName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (acc is null) return $"No account named '{accountName}'.";

        switch (command)
        {
            case "start": supervisor.Start(acc); return $"Starting **{acc.Name}**.";
            case "stop": await supervisor.Stop(acc); return $"Stopped **{acc.Name}** and closed its client.";
            case "rejoin": await supervisor.Stop(acc); supervisor.Start(acc); return $"Rejoining **{acc.Name}**.";
            default: return $"Unknown command '{command}'.";
        }
    }
}
