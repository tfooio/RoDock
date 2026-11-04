# RoDock

A dashboard for running several Roblox accounts: keeps alts in their private servers, closes and relaunches a client when it drops, and can be controlled from Discord.

Reference: [Roblox-Account-Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager) (GPL-3.0; no code copied).

## Install
Download `RoDock-win-Setup.exe` from the [latest release](https://github.com/tfooio/RoDock/releases/latest) and run it. Installed copies check for updates on every launch (and every 4 hours); a notice appears in the bottom-right corner when one is ready.

Needs Windows 10/11 (the login window uses the Microsoft Edge WebView2 runtime, already present on Windows 11).

## Use
- **Add account:** `+ Add account` -> `Log in with Roblox...` (or paste a cookie).
- **Per account:** set a Place ID and optionally a private server link, then Start. RoDock closes the old client, launches, waits until you are in game, and relaunches with backoff (10s..5min, paused after 5 failures) if you drop.
- **Dashboard** (default): the selected account up top, a tile per account with its game art, an activity list, and charts (accounts in game over 24 h, rejoins per day, hours online per account). Click a tile or an avatar in the right-hand dock to select it; the sliders icon opens its Place ID, private server link and Remove. The list icon in the left rail switches to a plain list.
- **Themes:** the palette icon in the left rail opens the theme panel. Pick a built-in theme (Dusty blue, Midnight, Forest, Rose, Sunset), click any of its five colours to change it with the colour picker, or press `New theme` to start your own copy. `Reset` restores a built-in theme; your choice and edits are saved in `settings.json`.
- **Discord:** create a bot, then `Discord` button -> token, your user ID, optional alert channel. Commands: `/status /start /stop /rejoin /startall /stopall`.

## Develop
- Run: `dotnet run --project RoDock`
- Self-checks: `dotnet run --project RoDock.Tests`
- UI preview PNGs (no accounts, no Roblox): `dotnet run --project RoDock.Tests -- preview <dir>`
- Release: push a tag `vX.Y.Z`; GitHub Actions builds and publishes it.
- Data: `%APPDATA%\RoDock` (DPAPI-encrypted). See [SECURITY.md](SECURITY.md).

## Notes
- Windows only. Not affiliated with Roblox Corporation.
- Your account cookies stay on your PC, encrypted with Windows DPAPI. Never share them: anyone with a cookie controls that account.
- Using several Roblox clients at once may be against Roblox's rules. Use at your own risk, ideally on alts.
- Builds are unsigned, so Windows SmartScreen may warn on first run.
- MIT licensed.
