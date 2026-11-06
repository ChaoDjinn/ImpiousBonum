using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Lights for a Discord call: one on while someone's talking, one while your microphone is picking you up. Unlike most
/// widgets it looks ten times a second, on its own timer and only while it's on screen, so a light keeps up with a voice.
/// The sound levels come from Windows' meters on Discord's sound (see <see cref="VoiceChatProvider"/>).
/// </summary>
public sealed class VoiceWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "voice", "Discord voice",
        "Lights for your Discord call: one comes on while someone's talking, the other while your microphone is picking you up, even when you're muted. Hidden outside a call.",
        300, 60,
        [
            Setting.Toggle("talking", "Someone talking", true, group: Setting.Content, help: "The light for sound from Discord: anyone talking, or Discord's own sounds."),
            Setting.PlainText("talkingLabel", "Talking label", "Talking"),
            Setting.Toggle("you", "You", true, group: Setting.Content,
                help: "The light for your microphone picking up sound, as Discord hears it, whether or not Discord sends it: muted, or push to talk."),
            Setting.PlainText("youLabel", "Your label", "You"),
            Setting.Choice("layout", "Layout", "row", ["row", "column"], group: Setting.Content, help: "row puts the lights side by side; column one above the other."),
            Setting.Toggle("outsideCall", "Show outside a call", false, group: Setting.Content,
                help: "Off hides the lights until you join a voice channel or call. Either way they're hidden while Discord isn't running."),
            Setting.Number("fontSize", "Font size", 22, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Color("talkingColor", "Talking", "accent", help: "The talking light while it's on."),
            Setting.Color("youColor", "You", "critical", help: "Your light while it's on: red, like an on-air light."),
            Setting.Color("offColor", "Off", "track", help: "The lights while they're off."),
        ],
        (settings, theme) => new VoiceWidget(settings, theme));

    private readonly Light? _talking;
    private readonly Light? _you;
    private readonly bool _outsideCall;
    private readonly DispatcherTimer _timer;
    private MetricStore? _store;

    public VoiceWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _outsideCall = settings.Bool("outsideCall");
        var fontSize = settings.Number("fontSize");
        var off = theme.Resolve(settings.String("offColor"), theme.Track);
        var column = settings.String("layout") == "column";
        var panel = new StackPanel { Orientation = column ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (settings.Bool("talking"))
            _talking = AddLight(settings.String("talkingLabel"), theme.Resolve(settings.String("talkingColor"), theme.Accent));
        if (settings.Bool("you"))
            _you = AddLight(settings.String("youLabel"), theme.Resolve(settings.String("youColor"), theme.Critical));
        Children.Add(Fit(panel, FitDirection.Both));

        Light AddLight(string? label, Brush on)
        {
            // A gap after the first: a few letters' worth side by side, a little under a line one above the other.
            var spacing = panel.Children.Count == 0 ? 0 : column ? fontSize * 0.35 : fontSize * 1.2;
            var light = new Light(CreateText(fontSize), label, fontSize, on, off, Theme.Foreground, Theme.Secondary)
            {
                Margin = column ? new Thickness(0, spacing, 0, 0) : new Thickness(spacing, 0, 0, 0),
            };
            panel.Children.Add(light);
            return light;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Show();
        // Only while on screen: a hidden page, a closed preview or a snapshot doesn't look.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                _timer.Start();
            else
                _timer.Stop();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _store = store;
        Show();
    }

    private void Show()
    {
        var state = _store?.GetAttachment(VoiceChatProvider.Live) as VoiceState;
        SetUnavailable(state is null || !state.InCall && !_outsideCall);
        _talking?.Show(state?.Talking == true);
        _you?.Show(state?.You == true);
    }

    /// <summary>A dot that's lit or not, and its label, brighter while it's lit.</summary>
    private sealed class Light : StackPanel
    {
        private readonly Ellipse _dot;
        private readonly TextBlock? _label;
        private readonly Brush _on;
        private readonly Brush _off;
        private readonly Brush _labelOn;
        private readonly Brush _labelOff;
        private bool? _lit;

        public Light(TextBlock text, string? label, double fontSize, Brush on, Brush off, Brush labelOn, Brush labelOff)
        {
            Orientation = Orientation.Horizontal;
            _on = on;
            _off = off;
            _labelOn = labelOn;
            _labelOff = labelOff;
            var size = Math.Round(fontSize * 0.7);
            _dot = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_dot);
            if (!string.IsNullOrWhiteSpace(label))
            {
                _label = text;
                _label.Text = label;
                _label.VerticalAlignment = VerticalAlignment.Center;
                _label.Margin = new Thickness(fontSize * 0.4, 0, 0, 0);
                Children.Add(_label);
            }
            Show(false);
        }

        public void Show(bool lit)
        {
            if (_lit == lit)
                return;
            _lit = lit;
            _dot.Fill = lit ? _on : _off;
            if (_label is not null)
                _label.Foreground = lit ? _labelOn : _labelOff;
        }
    }
}
