using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Each app's volume, like Windows' Volume mixer: a row for each app playing sound (and the speakers or headphones
/// first), with its icon, which mutes it when tapped, its name, a bar to drag and its volume. The apps are only read
/// while a mixer is on screen (see <see cref="SoundProvider.Want"/>).
/// </summary>
public sealed class MixerWidget : Widget
{
    /// <summary>The names the editor offers for <c>apps</c>: the apps seen playing sound since the app started.</summary>
    public const string AppNames = "soundApps";

    public const string Empty = "No apps are playing sound";

    /// <summary>How long a volume or mute set here is shown before Windows' own reading takes over again.</summary>
    internal static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);

    public static WidgetDescriptor Descriptor { get; } = new(
        "mixer", "Volume mixer",
        "Each app's volume, like Windows' Volume mixer: drag an app's bar to set it, or tap its icon to mute it. Your speakers or headphones come first.",
        460, 230,
        [
            Setting.Picks("apps", "Apps", PickList.AllValue, source: AppNames, all: "All apps",
                none: "No apps have played sound since Impious Bonum started.", help: "All the apps with sound, or just the ones chosen, in the order they're listed."),
            Setting.Toggle("output", "Speakers or headphones", true, group: Setting.Content, help: "The device Windows plays to, first, with its own volume."),
            Setting.Toggle("playingOnly", "Only apps playing", false, group: Setting.Content, help: "Leaves out apps that aren't making a sound now."),
            Setting.Number("max", "Most apps", 6, 1, 30, group: Setting.Content),
            Setting.Toggle("percent", "Show the volume", true, group: Setting.Content, help: "Each volume as a number, e.g. 65 %."),
            Setting.Number("fontSize", "Font size", 20, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("spacing", "Spacing", 10, 0, 100, group: Setting.Appearance),
            Setting.Number("barHeight", "Bar height", 6, 2, 60, group: Setting.Appearance),
            Setting.Color("color", "Bar colour", "accent"),
            Setting.Color("trackColor", "Track colour", "track", help: "The bar's empty part."),
            Setting.Color("mutedColor", "Muted colour", "secondary", help: "A muted app's bar."),
        ],
        (settings, theme) => new MixerWidget(settings, theme));

    /// <summary>Apps' icons by their program, decoded once.</summary>
    private static readonly Dictionary<string, BitmapSource?> AppIcons = new(StringComparer.OrdinalIgnoreCase);

    private readonly PickList _apps;
    private readonly bool _output;
    private readonly bool _playingOnly;
    private readonly int _max;
    private readonly bool _percent;
    private readonly double _fontSize;
    private readonly double _spacing;
    private readonly double _barHeight;
    private readonly Brush _bar;
    private readonly Brush _track;
    private readonly Brush _muted;
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, Row> _shown = [];
    private readonly Dictionary<string, (double Volume, DateTime At)> _setVolumes = [];
    private readonly Dictionary<string, (bool Muted, DateTime At)> _setMutes = [];
    private string? _keys;

    public MixerWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _apps = settings.Picks("apps");
        _output = settings.Bool("output");
        _playingOnly = settings.Bool("playingOnly");
        _max = (int)settings.Number("max");
        _percent = settings.Bool("percent");
        _fontSize = settings.Number("fontSize");
        _spacing = settings.Number("spacing");
        _barHeight = settings.Number("barHeight");
        _bar = theme.Resolve(settings.String("color"), theme.Accent);
        _track = theme.Resolve(settings.String("trackColor"), theme.Track);
        _muted = theme.Resolve(settings.String("mutedColor"), theme.Secondary);
        // In a stack, so the rows keep their own height; they get smaller together when they don't all fit. Only the height
        // shrinks them: the names and bars share out the width, a long name ending with … rather than pushing its bar along.
        var stack = new StackPanel();
        stack.Children.Add(_rows);
        Children.Add(Fit(stack, FitDirection.Height));
    }

    /// <summary>A tap on an app's icon mutes it, from a tablet too; the bars can only be dragged on the dashboard.</summary>
    public override bool PressableFromTablet => true;

    public override void Refresh(MetricStore store, DateTime now)
    {
        SoundProvider.Want(store, DateTimeOffset.UtcNow);
        var state = store.GetAttachment(SoundProvider.Mixer) as MixerState;
        var entries = Entries(state, _apps, _output, _playingOnly, _max);
        // Nothing until the apps have been read once, a second or so after the mixer first asks.
        var keys = state is null ? "" : string.Join("\n", ["", .. entries.Select(e => e.Key)]);
        if (keys != _keys)
        {
            _keys = keys;
            Build(entries, read: state is not null);
        }
        foreach (var entry in entries)
            Show(_shown[entry.Key], entry, now);
    }

    /// <summary>One row: the speakers or headphones (<see cref="AppVolumeControl.Output"/>), or an app.</summary>
    internal sealed record Entry(string Key, string Name, string? Exe, double Volume, bool Muted, bool Playing);

    /// <summary>
    /// The rows to show: the device first (with <paramref name="output"/>), then all the apps in name order, or the ones
    /// chosen in the order chosen, leaving out those not playing with <paramref name="playingOnly"/>, up to <paramref name="max"/> apps.
    /// </summary>
    internal static IReadOnlyList<Entry> Entries(MixerState? state, PickList chosen, bool output, bool playingOnly, int max)
    {
        if (state is null)
            return [];
        var entries = new List<Entry>();
        if (output && state.Output is { } device)
            entries.Add(new Entry(AppVolumeControl.Output, device.Name ?? "Speakers", null, device.Volume, device.Muted, true));
        var apps = chosen.All
            ? state.Apps
            : chosen.Names.Select(name => state.Apps.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).OfType<AppVolume>().ToList();
        entries.AddRange(apps.Where(a => !playingOnly || a.Playing).Take(max).Select(a => new Entry(a.Key, a.Name, a.Exe, a.Volume, a.Muted, a.Playing)));
        return entries;
    }

    private void Build(IReadOnlyList<Entry> entries, bool read)
    {
        _rows.Children.Clear();
        _shown.Clear();
        if (entries.Count == 0 && read)
        {
            var empty = CreateText(_fontSize, Theme.Secondary);
            empty.Text = Empty;
            empty.TextTrimming = TextTrimming.CharacterEllipsis;
            _rows.Children.Add(empty);
            return;
        }
        for (var i = 0; i < entries.Count; i++)
        {
            var row = new Row(this, entries[i]);
            row.Panel.Margin = new Thickness(0, i == 0 ? 0 : _spacing, 0, 0);
            _rows.Children.Add(row.Panel);
            _shown[entries[i].Key] = row;
        }
    }

    /// <summary>An entry's volume and mute, or what was just set here while Windows catches up.</summary>
    private void Show(Row row, Entry entry, DateTime now)
    {
        var volume = _setVolumes.TryGetValue(entry.Key, out var set) && now - set.At < Settle ? set.Volume : entry.Volume;
        var muted = _setMutes.TryGetValue(entry.Key, out var mute) && now - mute.At < Settle ? mute.Muted : entry.Muted;
        row.Show(volume, muted, entry.Playing);
    }

    private void SetVolume(Row row, double volume)
    {
        _setVolumes[row.Key] = (volume, DateTime.Now);
        row.Show(volume, row.Muted, row.Playing);
        RequestAction(new WidgetAction(WidgetAction.AppVolume, row.Key, volume.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void ToggleMute(Row row)
    {
        _setMutes[row.Key] = (!row.Muted, DateTime.Now);
        row.Show(row.Volume, !row.Muted, row.Playing);
        RequestAction(new WidgetAction(WidgetAction.AppMute, row.Key));
    }

    /// <summary>An app's icon, from its program, at about the size it's shown; null when it has none.</summary>
    private static BitmapSource? IconOf(string? exe, double size)
    {
        if (exe is null)
            return null;
        if (!AppIcons.TryGetValue(exe, out var icon))
            AppIcons[exe] = icon = ExeFile.LargestIcon(exe) is { } bytes ? MediaWidget.Decode(bytes, size) : null;
        return icon;
    }

    /// <summary>One app's row: its icon (a mute button), its name, its bar and its volume.</summary>
    private sealed class Row
    {
        private readonly MixerWidget _owner;
        private readonly FrameworkElement _icon;
        private readonly System.Windows.Shapes.Path? _glyph;
        private readonly TextBlock _name;
        private readonly VolumeBar _bar;
        private readonly TextBlock? _percent;

        public Row(MixerWidget owner, Entry entry)
        {
            _owner = owner;
            Key = entry.Key;
            var size = Math.Round(owner._fontSize * 1.3);
            Panel = new Grid();
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The speakers' icon shows whether they're muted; an app's is its own, faded while muted.
            if (IconOf(entry.Exe, size) is { } picture)
                _icon = new Image { Source = picture, Width = size, Height = size };
            else
            {
                _icon = Icons.Create(entry.Key == AppVolumeControl.Output ? "volume-up" : "music", owner.Theme.Foreground, size);
                _glyph = (_icon as Viewbox)?.Child as System.Windows.Shapes.Path;
            }
            var mute = new TapTarget { Child = _icon, Background = Brushes.Transparent, ToolTip = $"Mute or unmute {entry.Name}", VerticalAlignment = VerticalAlignment.Center };
            mute.Tapped += (_, _) => owner.ToggleMute(this);
            Panel.Children.Add(mute);

            _name = owner.CreateText(owner._fontSize);
            _name.Text = entry.Name;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _name.VerticalAlignment = VerticalAlignment.Center;
            _name.Margin = new Thickness(owner._fontSize * 0.5, 0, owner._fontSize * 0.5, 0);
            SetColumn(_name, 1);
            Panel.Children.Add(_name);

            _bar = new VolumeBar(owner._barHeight)
            {
                Fill = owner._bar,
                Track = owner._track,
                Height = Math.Max(owner._fontSize * 1.3, owner._barHeight * 2.4),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"{entry.Name}'s volume",
            };
            _bar.Dragged += (_, volume) => owner.SetVolume(this, volume);
            SetColumn(_bar, 2);
            Panel.Children.Add(_bar);

            if (owner._percent)
            {
                _percent = owner.CreateText(owner._fontSize * 0.85, owner.Theme.Secondary, HorizontalAlignment.Right);
                _percent.VerticalAlignment = VerticalAlignment.Center;
                // Wide enough for 100 %, so the bars line up whatever the volumes.
                _percent.MinWidth = owner._fontSize * 3;
                SetColumn(_percent, 3);
                Panel.Children.Add(_percent);
            }
        }

        public string Key { get; }

        public Grid Panel { get; }

        public double Volume { get; private set; } = -1;

        public bool Muted { get; private set; }

        public bool Playing { get; private set; }

        public void Show(double volume, bool muted, bool playing)
        {
            if (volume == Volume && muted == Muted && playing == Playing)
                return;
            (Volume, Muted, Playing) = (volume, muted, playing);
            _bar.Value = volume;
            _bar.Fill = muted ? _owner._muted : _owner._bar;
            _bar.InvalidateVisual();
            if (_percent is not null)
                _percent.Text = muted ? "Muted" : $"{Math.Round(volume * 100)} %";
            // Muted apps fade; apps with nothing playing are a little quieter than ones playing.
            _name.Foreground = muted || !playing ? _owner.Theme.Secondary : _owner.Theme.Foreground;
            if (_glyph is not null && Key == AppVolumeControl.Output)
                _glyph.Data = Icons.Get(muted ? "mute" : "volume-up");
            else
                _icon.Opacity = muted ? FadedOpacity : 1;
        }
    }

    /// <summary>
    /// A volume bar: a track, filled up to the volume, with a knob at the end. Pressing or dragging along it sets the
    /// volume, which it tells as <see cref="Dragged"/> for each move.
    /// </summary>
    internal sealed class VolumeBar : FrameworkElement
    {
        private readonly double _thickness;

        public VolumeBar(double thickness)
        {
            _thickness = thickness;
            Stylus.SetIsPressAndHoldEnabled(this, false);
            Stylus.SetIsTapFeedbackEnabled(this, false);
            Stylus.SetIsFlicksEnabled(this, false);
        }

        public double Value { get; set; }

        public required Brush Fill { get; set; }

        public required Brush Track { get; init; }

        /// <summary>The volume, from 0 to 1 in steps of 1%, each time it's pressed or dragged.</summary>
        public event EventHandler<double>? Dragged;

        private double Knob => _thickness * 1.2;

        /// <summary>The volume at a point along the bar, from 0 at its start to 1 at its end, in steps of 1%.</summary>
        internal double VolumeAt(double x)
        {
            var length = ActualWidth - 2 * Knob;
            return length <= 0 ? 0 : Math.Round(Math.Clamp((x - Knob) / length, 0, 1), 2);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (CaptureMouse())
                DragTo(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsMouseCaptured)
                DragTo(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!IsMouseCaptured)
                return;
            DragTo(e.GetPosition(this).X);
            ReleaseMouseCapture();
            e.Handled = true;
        }

        /// <summary>The press or drag has reached <paramref name="x"/> along the bar.</summary>
        internal void DragTo(double x)
        {
            var volume = VolumeAt(x);
            if (volume != Value)
                Dragged?.Invoke(this, volume);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 2 * Knob || height <= 0)
                return;
            // Transparent all over, so a press anywhere along it counts, not just on the thin bar.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            var top = (height - _thickness) / 2;
            var radius = _thickness / 2;
            var end = Knob + (width - 2 * Knob) * Math.Clamp(Value, 0, 1);
            dc.DrawRoundedRectangle(Track, null, new Rect(Knob, top, width - 2 * Knob, _thickness), radius, radius);
            if (end > Knob)
                dc.DrawRoundedRectangle(Fill, null, new Rect(Knob, top, end - Knob, _thickness), radius, radius);
            dc.DrawEllipse(Fill, null, new Point(end, height / 2), Knob, Knob);
        }
    }
}
