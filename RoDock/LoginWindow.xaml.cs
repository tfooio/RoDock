using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace RoDock;

/// Shows the real Roblox login page in a throwaway browser profile and returns the .ROBLOSECURITY cookie once the user has logged in.
/// The user types their own credentials into Roblox's page; this app never sees them.
public partial class LoginWindow : Window
{
    readonly string _profile = Path.Combine(Path.GetTempPath(), "RoDock-login-" + Guid.NewGuid());
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };

    public string? Cookie { get; private set; }
    public string? Error { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Start();
        Closed += (_, _) =>
        {
            _poll.Stop();
            Web.Dispose();
            try { Directory.Delete(_profile, true); } catch { } // temp profile; leftovers are harmless
        };
    }

    async Task Start()
    {
        try
        {
            // Fresh profile each time, so the previous account's login never leaks into the next one.
            await Web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(userDataFolder: _profile));
            Web.CoreWebView2.Navigate("https://www.roblox.com/login");
            _poll.Tick += async (_, _) => await CheckCookie();
            _poll.Start();
        }
        catch (Exception ex)
        {
            Error = $"Could not start the browser component (WebView2): {ex.Message}";
            DialogResult = false;
        }
    }

    async Task CheckCookie()
    {
        var cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
        if (cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY") is { } c)
        {
            _poll.Stop();
            Cookie = c.Value;
            DialogResult = true;
        }
    }

    /// For the automated check: page title and URL once loaded, or null.
    internal string? PageTitle => Web.CoreWebView2 is { } c ? $"{c.DocumentTitle} @ {c.Source}" : null;
}
