using System.Windows;
using Discord;
using Discord.WebSocket;

namespace RoDock;

/// Saved encrypted in discord.dat. UserId is the only Discord user allowed to run commands.
public record DiscordConfig(string Token, ulong UserId, ulong ChannelId);

/// Slash-command remote control + drop/recovery alerts. Commands answer only to the configured user, privately (ephemeral).
public class DiscordBot(Commands commands)
{
    DiscordSocketClient? _client;
    DiscordConfig? _config;
    readonly HashSet<Account> _hadProblem = [];

    public bool Connected => _client?.ConnectionState == ConnectionState.Connected;

    public async Task Connect(DiscordConfig config)
    {
        await Disconnect();
        _config = config;
        _client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds });
        _client.GuildAvailable += g => g.BulkOverwriteApplicationCommandAsync(Definitions()); // guild commands show up instantly
        _client.SlashCommandExecuted += OnCommand;
        await _client.LoginAsync(TokenType.Bot, config.Token);
        await _client.StartAsync();
    }

    public async Task Disconnect()
    {
        if (_client is null) return;
        await _client.StopAsync();
        await _client.DisposeAsync();
        _client = null;
    }

    static ApplicationCommandProperties[] Definitions()
    {
        var all = new List<ApplicationCommandProperties>();
        foreach (var name in Commands.WithoutAccount)
            all.Add(new SlashCommandBuilder().WithName(name).WithDescription($"{name} - all accounts").Build());
        foreach (var name in Commands.WithAccount)
            all.Add(new SlashCommandBuilder().WithName(name).WithDescription($"{name} one account")
                .AddOption("account", ApplicationCommandOptionType.String, "Account name", isRequired: true).Build());
        return [.. all];
    }

    async Task OnCommand(SocketSlashCommand cmd)
    {
        if (cmd.User.Id != _config?.UserId) { await cmd.RespondAsync("You are not allowed to use this bot.", ephemeral: true); return; }

        await cmd.DeferAsync(ephemeral: true); // start/stop can take a few seconds
        var arg = cmd.Data.Options.FirstOrDefault()?.Value as string;
        string reply;
        try { reply = await await Application.Current.Dispatcher.InvokeAsync(() => commands.Handle(cmd.Data.Name, arg)); }
        catch (Exception ex) { reply = $"Error: {ex.Message}"; }
        await cmd.FollowupAsync(reply, ephemeral: true);
    }

    /// Sends an alert when the account starts having trouble, and when it recovers. Call once per account.
    public void Watch(Account a) => a.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName != nameof(Account.StatusKind)) return;
        switch (a.StatusKind)
        {
            case Kind.Warn or Kind.Bad:
                _hadProblem.Add(a);
                _ = Notify($"{(a.StatusKind == Kind.Bad ? "🔴" : "🟡")} **{a.Name}**: {a.Status}");
                break;
            case Kind.Good when _hadProblem.Remove(a):
                _ = Notify($"🟢 **{a.Name}** is back in game.");
                break;
        }
    };

    async Task Notify(string text)
    {
        try
        {
            if (_config is { ChannelId: > 0 } c && _client?.GetChannel(c.ChannelId) is IMessageChannel ch) await ch.SendMessageAsync(text);
        }
        catch { } // an alert that fails to send must never break the rejoin loop
    }
}
