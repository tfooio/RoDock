using Velopack;

namespace RoDock;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run(); // handles installer/updater hooks; must run before anything else
        // Animations are short one-shots (nothing loops), so 60 fps costs CPU only for the few hundred ms they run, and 30 looked choppy.
        // Must happen before any animation exists.
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new System.Windows.FrameworkPropertyMetadata(60));
        Animations.Hook();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
