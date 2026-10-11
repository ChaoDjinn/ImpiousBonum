using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Help → About Impious Bonum: the version, what's running it, and where to find help.</summary>
public partial class AboutWindow : Window
{
    private readonly string _details;

    /// <param name="version">The installed version, or null for a development build.</param>
    /// <param name="sensorService">The sensor service's status, as the tray shows it.</param>
    public AboutWindow(string? version, string sensorService)
    {
        InitializeComponent();
        Logo.Source = LoadLogo();
        VersionText.Text = version is null ? "Development build" : $"Version {version}";
        _details = Details(version, sensorService);
        DetailsText.Text = _details;

        GuideLink.NavigateUri = new Uri(Links.Guide(version));
        ChangelogLink.NavigateUri = new Uri(Links.Changelog(version));
        RepositoryLink.NavigateUri = new Uri(Links.Repository);
        IssueLink.NavigateUri = new Uri(Links.NewIssue);
        LicenseLink.NavigateUri = new Uri(Links.License);
    }

    /// <summary>What a problem report needs: the version, Windows, .NET and the sensor service.</summary>
    internal static string Details(string? version, string sensorService) =>
        $"Impious Bonum {version ?? "(development build)"} · {RuntimeInformation.OSDescription.Trim()} · "
        + $"{RuntimeInformation.FrameworkDescription} · sensor service: {sensorService}";

    private static BitmapFrame? LoadLogo()
    {
        using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream(TrayIcon.IconResource);
        if (stream is null)
            return null;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.MaxBy(frame => frame.PixelWidth);
    }

    private void OnLink(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { NavigateUri: { } uri })
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No browser registered; nothing else to offer.
        }
    }

    // Shown with Show(), not as a dialog, so IsCancel alone wouldn't close it.
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (TabletLinkWindow.CopyToClipboard(_details))
            CopyButton.Content = "Copied";
    }
}
