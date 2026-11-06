using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// One row per game Steam is downloading, updating or installing (<see cref="SteamDownloadsProvider"/>): its name, how
/// far it has got and how fast where Steam has said lately, and a bar. Rows follow Steam's downloads as they start and
/// finish, and the widget hides while there are none.
/// </summary>
public sealed class DownloadsWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "downloads", "Steam downloads",
        "One row per game Steam is downloading, updating or installing, with a bar and how much is done. Read from Steam's own files on this PC, so it needs no account and Steam needn't be open, but Steam writes how far a download has got only when it starts, pauses or resumes, so one that has been going a while shows its size until it next does. Shows nothing while there's nothing to download.",
        500, 230,
        [
            Setting.Number("fontSize", "Font size", 25, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Toggle("showSize", "Show sizes", true, help: "How much is done of how much, such as 6.1 / 14.0 GB. Off shows how far it has got as a percentage."),
            Setting.Toggle("showSpeed", "Show speed", true,
                help: "The speed and the time left, when Steam has written its progress twice within two minutes. It seldom does, as it writes when a download starts, pauses or resumes."),
            Setting.Number("barHeight", "Bar height", 5, 1, 100, group: Setting.Appearance),
            Setting.Number("spacing", "Row spacing", 12, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Bar colour", "accent"),
            Setting.Color("waitingColor", "Waiting colour", "secondary", help: "The bar of a download that's paused, or started and waiting behind another."),
            Setting.Color("trackColor", "Track colour", "track", help: "The empty part of each bar. track is the theme's bar track."),
            Setting.Color("labelColor", "Label colour", "foreground"),
            Setting.Color("valueColor", "Value colour", "secondary"),
        ],
        (settings, theme) => new DownloadsWidget(settings, theme));

    private readonly StackPanel _rows = new();
    private readonly double _fontSize;
    private readonly bool _showSize;
    private readonly bool _showSpeed;
    private readonly double _barHeight;
    private readonly double _spacing;
    private readonly Brush _fill;
    private readonly Brush _waiting;
    private readonly Brush _track;
    private readonly Brush _label;
    private readonly Brush _value;
    private readonly List<Row> _current = [];
    // Not null at first, so the first refresh sets the widget up even without a list.
    private object? _list = new();
    private string? _shown;

    public DownloadsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _fontSize = settings.Number("fontSize");
        _showSize = settings.Bool("showSize");
        _showSpeed = settings.Bool("showSpeed");
        _barHeight = settings.Number("barHeight");
        _spacing = settings.Number("spacing");
        _fill = theme.Resolve(settings.String("color"), theme.Accent);
        _waiting = theme.Resolve(settings.String("waitingColor"), theme.Secondary);
        _track = theme.Resolve(settings.String("trackColor"), theme.Track);
        _label = theme.Resolve(settings.String("labelColor"), theme.Foreground);
        _value = theme.Resolve(settings.String("valueColor"), theme.Secondary);
        Children.Add(Fit(_rows, FitDirection.Height));
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        // The provider replaces the list when it looks again, every few seconds; nothing changes in between.
        var list = store.GetAttachment(SteamDownloadsProvider.List);
        if (ReferenceEquals(list, _list))
            return;
        _list = list;
        var downloads = list as IReadOnlyList<SteamDownload> ?? [];
        // Downloads start and finish now and then: rebuild only when the games on show change.
        var key = string.Join("\n", downloads.Select(d => $"{d.AppId}|{d.Name}"));
        if (key != _shown)
        {
            _shown = key;
            Rebuild(downloads);
        }
        SetUnavailable(downloads.Count == 0);

        for (var i = 0; i < _current.Count && i < downloads.Count; i++)
        {
            var download = downloads[i];
            var row = _current[i];
            row.Value.Text = Describe(download, _showSize, _showSpeed);
            // Without word lately of how far it has got, the bar is empty rather than showing where it was.
            var percent = download.Percent ?? 0;
            row.Bar.ColumnDefinitions[0].Width = new GridLength(percent, GridUnitType.Star);
            row.Bar.ColumnDefinitions[1].Width = new GridLength(100 - percent, GridUnitType.Star);
            row.Fill.Background = download.State is SteamDownloadState.Paused or SteamDownloadState.Queued ? _waiting : _fill;
        }
    }

    /// <summary>
    /// A download's progress in a few words: "6.1 / 14.0 GB · 40.0 MB/s · 3m" while it downloads, its state first while
    /// it's doing anything else ("Paused · 6.1 / 14.0 GB", "Queued · 2.1 GB") or Steam hasn't said lately how far it has
    /// got ("Downloading · 14.0 GB"), and a percentage instead of sizes without <paramref name="sizes"/>.
    /// </summary>
    internal static string Describe(SteamDownload download, bool sizes, bool speed)
    {
        var parts = new List<string>();
        // Downloading goes without saying beside how far it has got, but not on its own.
        if (download.State != SteamDownloadState.Downloading || download.Percent is null)
            parts.Add(SteamDownload.StateName(download.State));
        if (download.Total > 0)
        {
            // A download that hasn't started, or whose progress is out of date, has only a size.
            parts.Add(download.Stale || (download.Done == 0 && download.State == SteamDownloadState.Queued) ? MetricFormatter.Format(download.Total, MetricUnit.Bytes)
                : sizes ? Sizes(download.Done, download.Total)
                : MetricFormatter.Format(download.Percent, MetricUnit.Percent, new FormatSpec("0")));
        }
        if (speed && !download.Stale && download.Speed is { } bytesPerSecond)
        {
            parts.Add(MetricFormatter.Format(bytesPerSecond, MetricUnit.BytesPerSecond));
            if (download.TimeLeft is { } left)
                parts.Add(DurationText.Format(left));
        }
        return parts.Count > 0 ? string.Join(" · ", parts) : SteamDownload.StateName(download.State);
    }

    /// <summary>How much is done of how much, both in the unit the whole is shown in: "6.1 / 14.0 GB".</summary>
    internal static string Sizes(long done, long total)
    {
        var unit = MetricFormatter.ByteUnitFor(total);
        return $"{MetricFormatter.Format(done, MetricUnit.Bytes, new FormatSpec(ByteUnit: unit, NoUnit: true))} / {MetricFormatter.Format(total, MetricUnit.Bytes, new FormatSpec(ByteUnit: unit))}";
    }

    private void Rebuild(IReadOnlyList<SteamDownload> downloads)
    {
        _rows.Children.Clear();
        _current.Clear();

        foreach (var download in downloads)
        {
            var label = CreateText(_fontSize, _label);
            label.Text = download.Name;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var value = CreateText(_fontSize * 0.9, _value, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            var header = LabelAndValue(label, value);

            var bar = new Grid { Height = _barHeight, Background = _track, Margin = new Thickness(0, 2, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) });
            var fill = new Border { Background = _fill };
            bar.Children.Add(fill);

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, _spacing) };
            row.Children.Add(header);
            row.Children.Add(bar);
            _rows.Children.Add(row);

            _current.Add(new Row(value, bar, fill));
        }
    }

    private sealed record Row(TextBlock Value, Grid Bar, Border Fill);
}
