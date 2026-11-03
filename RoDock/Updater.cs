using Velopack;
using Velopack.Sources;

namespace RoDock;

/// Checks GitHub Releases for a newer version. Does nothing when run from source (`dotnet run`), only when installed from Setup.exe.
public class Updater
{
    const string Repo = "https://github.com/tfooio/RoDock";
    readonly UpdateManager _mgr = new(new GithubSource(Repo, null, false));
    UpdateInfo? _pending;

    /// The newer version number, or null if up to date (or offline).
    public async Task<string?> Check()
    {
        if (!_mgr.IsInstalled) return null;
        try { _pending = await _mgr.CheckForUpdatesAsync(); return _pending?.TargetFullRelease.Version.ToString(); }
        catch { return null; } // an update check that fails is never worth interrupting the user
    }

    public Task Download() => _mgr.DownloadUpdatesAsync(_pending!);

    public void ApplyAndRestart() => _mgr.ApplyUpdatesAndRestart(_pending!);
}
