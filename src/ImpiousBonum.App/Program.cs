using ImpiousBonum.App.Shell;
using Velopack;

namespace ImpiousBonum.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: when the installer or updater launches us with a hook argument it handles it and exits.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Uninstalling())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>
    /// Leave nothing behind that would outlive the app: the start-with-Windows entry, and the sensor service
    /// (which lives in Program Files, so removing it needs one UAC prompt). Settings in %AppData% are kept.
    /// Velopack gives this hook about 30 seconds.
    /// </summary>
    private static void Uninstalling()
    {
        try
        {
            StartupRegistration.SetEnabled(false);
        }
        catch (Exception)
        {
            // Best effort; the uninstall must carry on regardless.
        }

        if (SensorServiceControl.IsInstalled)
        {
            try
            {
                SensorServiceControl.RunAsync(install: false).Wait(TimeSpan.FromSeconds(25));
            }
            catch (Exception)
            {
            }
        }
    }
}
