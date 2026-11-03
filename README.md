# RoDock

WPF (.NET 10) multi-account launcher that keeps alts in their private servers and rejoins them when they drop.
Reference: [Roblox-Account-Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager) (GPL-3.0; no code copied).

- Run: `dotnet run --project RoDock`
- Self-check: `dotnet run --project RoDock.Tests`
- UI preview PNGs (no accounts, no Roblox): `dotnet run --project RoDock.Tests -- preview <dir>`
- Accounts: `%APPDATA%\RoDock\accounts.dat`, DPAPI-encrypted (this Windows user only).

Per account: Place ID + optional private server link. Start = close old client, launch, wait until in game, watch; on a drop it closes the client and relaunches with backoff (10s..5min, paused after 5 failures).

## Notes
- Windows only. Not affiliated with Roblox Corporation.
- Your account cookies stay on your PC, encrypted with Windows DPAPI. Never share them: anyone with a cookie controls that account.
- Using several Roblox clients at once may be against Roblox's rules. Use at your own risk, ideally on alts.
- MIT licensed.
