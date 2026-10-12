using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// About and updates: the version, where updating has got to (checking, downloading, ready to restart into), and
/// where to find help. An update found here can be installed from here, without going back to the tray.
/// </summary>
internal sealed class AboutPage(ISettingsHost host) : CardPage("About and updates", "Keeping Impious Bonum up to date, and where to find help.")
{
    private readonly StackPanel _update = new();
    private UpdateStatus? _shown;

    protected override void Build()
    {
        var version = host.Version;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(new Image { Source = LoadLogo(), Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top });
        var name = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = "Impious Bonum", FontSize = 20, FontWeight = FontWeights.SemiBold });
        name.Children.Add(SettingsWindow.Styled(new TextBlock { Text = version is null ? "Development build" : $"Version {version}" }, "SecondaryText"));
        name.Children.Add(SettingsWindow.Styled(new TextBlock
        {
            Text = "A lightweight system dashboard for a spare screen. Dog-Latin for \"wicked good\".",
            Margin = new Thickness(0, 4, 0, 0),
        }, "NoteText"));
        header.Children.Add(name);
        Add(header);

        Add(Ui.Heading("Updates"));
        _shown = null;
        Add(_update);
        ShowUpdate();
        Add(Ui.Card("", "What's new", "Everything that's changed, version by version, on GitHub",
            Ui.Button("Open", () => Ui.Open(Links.Changelog(version)), "")));

        Add(Ui.Heading("Help"));
        Add(Ui.Card("", "User guide", "How to use everything, on GitHub", Ui.Button("Open", () => Ui.Open(Links.Guide(version)), "")));
        Add(Ui.Card("", "Widget reference", "Every widget's settings, on GitHub", Ui.Button("Open", () => Ui.Open(Links.Widgets(version)), "")));
        Add(Ui.Card("", "Report a problem", "Open an issue on GitHub. The details below, and dashboard.log from the settings folder, help.",
            Ui.Button("Open", () => Ui.Open(Links.NewIssue), "")));
        var details = Details(version, host.SensorStatus);
        TextBlock? copied = null;
        var copy = Ui.Button("Copy", () =>
        {
            if (TabletLinkWindow.CopyToClipboard(details) && copied is not null)
                copied.Text = "Copied";
        }, "");
        copied = (copy.Content as StackPanel)?.Children.OfType<TextBlock>().LastOrDefault();
        Add(Ui.Card("", "Details", details, copy));

        var licence = SettingsWindow.Styled(new TextBlock { Margin = new Thickness(2, 12, 0, 0) }, "NoteText");
        licence.Inlines.Add("Free and open source under the ");
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("MIT licence")) { NavigateUri = new Uri(Links.License) };
        link.Click += (_, _) => Ui.Open(Links.License);
        licence.Inlines.Add(link);
        licence.Inlines.Add(". Source on ");
        var repo = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("GitHub")) { NavigateUri = new Uri(Links.Repository) };
        repo.Click += (_, _) => Ui.Open(Links.Repository);
        licence.Inlines.Add(repo);
        licence.Inlines.Add(".");
        Add(licence);
    }

    public override void Refresh() => ShowUpdate();

    /// <summary>The update card for where updating has got to, rebuilt only when that changes.</summary>
    private void ShowUpdate()
    {
        var status = host.Updates;
        if (status == _shown)
            return;
        _shown = status;
        _update.Children.Clear();
        _update.Children.Add(UpdateCard(status));
    }

    private UIElement UpdateCard(UpdateStatus status)
    {
        if (!status.Installed)
        {
            return Ui.Card("", "Updates",
                "This is a development build, which doesn't update itself. Installed copies check GitHub for a new version every few hours.");
        }
        if (status.Ready is { } ready)
        {
            return Ui.Card("", $"Impious Bonum {ready} is ready to install",
                "Restart to finish updating. It takes a few seconds, and your settings and layouts stay as they are.",
                Ui.Button("Restart to update", () => _ = host.RestartToUpdateAsync(), "", accent: true));
        }
        if (status.Checking)
        {
            var bar = new ProgressBar { Width = 160, IsIndeterminate = status.Progress is null, Value = status.Progress ?? 0 };
            return Ui.Card("", status.Progress is { } percent ? $"Downloading the update… {percent}%" : "Checking for updates…",
                "You can carry on; it'll say here when it's ready.", bar);
        }

        var check = Ui.Button("Check for updates", () => _ = host.CheckForUpdatesAsync(), "");
        if (status.Failed)
        {
            return Ui.Card("", "Couldn't check for updates",
                $"Tried {When(status.LastChecked)}. Check you're online, then try again; it also tries again by itself in a few hours.", check);
        }
        return Ui.Card("", "You're up to date",
            status.LastChecked is null ? "It checks GitHub for a new version every few hours." : $"Last checked {When(status.LastChecked)}", check);
    }

    /// <summary>"today at 14:32", "yesterday at 09:05", or the date.</summary>
    internal static string When(DateTime? time, DateTime? now = null)
    {
        if (time is not { } t)
            return "a moment ago";
        var today = (now ?? DateTime.Now).Date;
        return t.Date == today ? $"today at {t:t}"
            : t.Date == today.AddDays(-1) ? $"yesterday at {t:t}"
            : $"on {t:d MMMM} at {t:t}";
    }

    /// <summary>What a problem report needs: the version, Windows, .NET and the sensor service.</summary>
    internal static string Details(string? version, string sensorService) =>
        $"Impious Bonum {version ?? "(development build)"} · {RuntimeInformation.OSDescription.Trim()} · "
        + $"{RuntimeInformation.FrameworkDescription} · sensor service: {sensorService}";

    /// <summary>The app's icon at its largest, for the About page and the window's sidebar.</summary>
    internal static ImageSource? LoadLogo()
    {
        using var stream = typeof(AboutPage).Assembly.GetManifestResourceStream(TrayIcon.IconResource);
        if (stream is null)
            return null;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.MaxBy(frame => frame.PixelWidth);
    }
}
