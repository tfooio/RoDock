using Velopack;

namespace RoDock;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run(); // handles installer/updater hooks; must run before anything else
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
