# Security

## What RoDock protects
- Account cookies and the Discord bot token are saved encrypted with Windows DPAPI (`%APPDATA%\RoDock`). Another Windows user, or a copy of the file on another PC, cannot read them.
- Cookies are only ever sent to `https://*.roblox.com`. Redirects are followed by hand and blocked if they leave Roblox.
- The login window only browses roblox.com, uses a throwaway profile, and clears its cookies after capture.
- Discord commands work only for the single user ID you configure, replies are private, and alerts never ping anyone.
- Nothing is uploaded anywhere except to Roblox and (if you enable it) Discord. There is no telemetry.

## What it does not protect against
- Malware already running as your Windows user can use DPAPI like RoDock does. Keep your PC clean.
- Anyone who gets a `.ROBLOSECURITY` cookie controls that account. Never share one.
- Alert messages go to the channel you choose; anyone who can read it sees your account names.
- Windows SmartScreen / antivirus may warn about unsigned builds, because the app closes processes and handles logins.

## Reporting
Open a GitHub issue, or for something sensitive use GitHub's "Report a vulnerability" (Security tab).
