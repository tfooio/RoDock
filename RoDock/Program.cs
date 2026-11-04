using Velopack;

namespace RoDock;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run(); // handles installer/updater hooks; must run before anything else
        // Every animation runs at 30 fps instead of 60: about half the CPU, and not noticeable on UI motion. Must happen before any animation exists.
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new System.Windows.FrameworkPropertyMetadata(30));
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
