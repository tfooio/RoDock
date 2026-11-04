using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RoDock;

public partial class App : Application
{
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(nint process);

    protected override void OnStartup(StartupEventArgs e)
    {
        // The dashboard is mostly static, so GPU rendering buys nothing but a loaded graphics driver (measured: about 40 MB less memory).
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);

        // Hand unused memory back to Windows shortly after start and each time RoDock loses focus; pages come back only if touched again.
        var trim = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        trim.Tick += (_, _) => { trim.Stop(); EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); };
        Deactivated += (_, _) => { trim.Stop(); trim.Start(); };
        trim.Start();
    }
}
