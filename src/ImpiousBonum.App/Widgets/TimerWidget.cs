using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A stopwatch, a countdown (tap to start and pause) or a count down to a time of day or a date. Running timers keep
/// going while the dashboard is rebuilt: their state lives in <see cref="TimerClock"/>, not the widget.
/// </summary>
public sealed class TimerWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "timer", "Timer",
        "A stopwatch, a countdown, or the time left until a time or date. Tap the time to start and pause; the button underneath resets it.",
        360, 170,
        [
            Setting.Choice("mode", "Mode", "countdown", ["countdown", "stopwatch", "until"], group: Setting.Content,
                help: "countdown counts down from the length below; stopwatch counts up; until counts down to the time or date below and runs by itself."),
            Setting.PlainText("duration", "Length", "25:00",
                help: "For countdown: minutes:seconds or hours:minutes:seconds, e.g. 5:00 or 1:30:00. A plain number is minutes."),
            Setting.PlainText("target", "Until", "18:00",
                help: "For until: a time of day (the next one comes round daily) or a date and time, e.g. 2026-12-25 or 2026-12-25 09:00."),
            Setting.PlainText("label", "Label", null,
                help: "Shown above the time. Timers with the same label, mode and length share one clock, so pages and other layouts can show the same timer."),
            Setting.PlainText("doneText", "When done", "Time's up",
                help: "For countdown and until: shown and sent as a notification when it reaches zero. Empty sends none."),
            Setting.Toggle("notify", "Notify when done", true, group: Setting.Content,
                help: "A Windows notification when a countdown or until reaches zero, as long as the timer is on screen."),
            Setting.Toggle("showReset", "Reset button", true, group: Setting.Content, help: "Not shown for until."),
            Setting.Number("fontSize", "Time size", 72, 6, 600),
            Setting.Number("labelSize", "Label size", 22, 6, 200),
            Setting.Choice("align", "Alignment", "center", Setting.HorizontalAlignments),
            Setting.Color("doneColor", "Done colour", "critical", help: "The time's colour once it has reached zero; it also blinks."),
            Setting.Color("pausedColor", "Paused colour", "secondary", help: "The time's colour while stopped or paused."),
        ],
        (settings, theme) => new TimerWidget(settings, theme));

    private readonly string _mode;
    private readonly TimeSpan _duration;
    private readonly string _target;
    private readonly string _doneText;
    private readonly bool _notify;
    private readonly Brush _doneColor;
    private readonly Brush _pausedColor;
    private readonly TimerClock.State _state;
    private readonly TextBlock _label;
    private readonly TextBlock _time;

    public TimerWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _mode = settings.String("mode");
        _duration = ParseDuration(settings.String("duration")) ?? TimeSpan.FromMinutes(25);
        _target = settings.String("target");
        _doneText = settings.String("doneText");
        _notify = settings.Bool("notify");
        _doneColor = theme.Resolve(settings.String("doneColor"), theme.Critical);
        _pausedColor = theme.Resolve(settings.String("pausedColor"), theme.Secondary);
        var labelText = settings.String("label");
        _state = TimerClock.For($"{_mode}|{labelText}|{(_mode == "countdown" ? _duration : _mode == "until" ? _target : string.Empty)}");

        var alignment = ParseAlignment(settings.String("align"));
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _label = CreateText(settings.Number("labelSize"), Theme.Secondary, alignment);
        _label.Text = labelText;
        if (labelText.Length > 0)
            stack.Children.Add(_label);

        var timeSize = settings.Number("fontSize");
        _time = CreateText(timeSize, alignment: alignment);
        _time.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _time.LineHeight = timeSize * 1.1;
        if (_mode == "until")
            stack.Children.Add(_time);
        else
        {
            var timeTap = new TapTarget { Child = _time, Background = Brushes.Transparent, HorizontalAlignment = alignment };
            timeTap.Tapped += (_, _) => Toggle(DateTime.Now);
            stack.Children.Add(timeTap);

            if (settings.Bool("showReset"))
            {
                var resetText = CreateText(settings.Number("labelSize"), Theme.Secondary);
                resetText.Text = "Reset";
                var reset = new TapTarget
                {
                    Child = resetText,
                    Background = Brushes.Transparent,
                    HorizontalAlignment = alignment,
                    Padding = new Thickness(0, 2, 0, 2),
                };
                reset.Tapped += (_, _) => Reset(DateTime.Now);
                stack.Children.Add(reset);
            }
        }
        Children.Add(stack);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var (left, done) = Reading(now);
        var running = _mode == "until" || _state.Running;
        _time.Text = done && _doneText.Length > 0 ? _doneText : Format(left, roundUp: _mode != "stopwatch");
        _time.Foreground = done ? _doneColor : running ? Theme.Foreground : _pausedColor;
        // Blink once done: on one second, dimmed the next.
        _time.Opacity = done && now.Second % 2 == 1 ? FadedOpacity : 1;

        if (done && _notify && _doneText.Length > 0 && _state.Announce())
            RequestAction(new WidgetAction(WidgetAction.Notify, Notification()));
    }

    /// <summary>What's on the clock now, and whether it has reached zero.</summary>
    private (TimeSpan Shown, bool Done) Reading(DateTime now)
    {
        switch (_mode)
        {
            case "stopwatch":
                return (_state.Elapsed(now), false);
            case "until":
            {
                if (ParseTarget(_target, _state.Anchor(now)) is not { } target)
                    return (TimeSpan.Zero, false);
                var left = target - now;
                // A daily time that has just passed stays "done" for a minute, then counts to tomorrow's.
                if (left <= TimeSpan.Zero && now - target < TimeSpan.FromMinutes(1))
                    return (TimeSpan.Zero, true);
                if (left <= TimeSpan.Zero && IsTimeOfDay(_target))
                {
                    _state.Reset(now);
                    return Reading(now);
                }
                return left <= TimeSpan.Zero ? (TimeSpan.Zero, true) : (left, false);
            }
            default:
            {
                var left = _duration - _state.Elapsed(now);
                return left <= TimeSpan.Zero ? (TimeSpan.Zero, true) : (left, false);
            }
        }
    }

    private string Notification()
    {
        var label = _label.Text;
        return label.Length > 0 ? $"{label}: {_doneText}" : _doneText;
    }

    /// <summary>A tap on the time: start or pause, or once a countdown is done, back to the start.</summary>
    internal void Toggle(DateTime now)
    {
        if (_mode == "countdown" && Reading(now).Done)
            _state.Reset(now);
        else
            _state.Toggle(now);
        Refresh(new MetricStore(), now);
    }

    internal void Reset(DateTime now)
    {
        _state.Reset(now);
        Refresh(new MetricStore(), now);
    }

    internal string ShownText => _time.Text;

    /// <summary>"25:00" → 25 minutes, "1:30:00" → 90 minutes, "90" → 90 minutes. Null when it can't be read or isn't positive.</summary>
    internal static TimeSpan? ParseDuration(string text)
    {
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes))
            return minutes > 0 && minutes < 100_000 ? TimeSpan.FromMinutes(minutes) : null;
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3 || parts.Any(p => !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return null;
        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        var span = numbers.Length == 2
            ? new TimeSpan(0, numbers[0], numbers[1])
            : new TimeSpan(numbers[0], numbers[1], numbers[2]);
        return span > TimeSpan.Zero ? span : null;
    }

    /// <summary>
    /// The moment <paramref name="text"/> names, as seen from <paramref name="from"/>: a time of day is its next
    /// occurrence (today if it's still to come); a date with no time is its midnight. Null when it can't be read.
    /// </summary>
    internal static DateTime? ParseTarget(string text, DateTime from)
    {
        text = text.Trim();
        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var time) || TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out time))
        {
            var today = from.Date + time.ToTimeSpan();
            return today > from ? today : today.AddDays(1);
        }
        string[] formats = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private static bool IsTimeOfDay(string text) =>
        TimeOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, out _) || TimeOnly.TryParse(text.Trim(), CultureInfo.CurrentCulture, out _);

    /// <summary>4:05, 1:04:05, or 3d 01:04:05, in whole seconds. Counting down rounds up, so 0:00 only shows once it's done.</summary>
    internal static string Format(TimeSpan span, bool roundUp)
    {
        var total = Math.Max(0, span.TotalSeconds);
        var seconds = (long)(roundUp ? Math.Ceiling(total - 0.0005) : Math.Floor(total));
        var whole = TimeSpan.FromSeconds(seconds);
        if (whole.Days > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{whole.Days}d {whole.Hours:00}:{whole.Minutes:00}:{whole.Seconds:00}");
        if (whole.Hours > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{whole.Hours}:{whole.Minutes:00}:{whole.Seconds:00}");
        return string.Create(CultureInfo.InvariantCulture, $"{whole.Minutes}:{whole.Seconds:00}");
    }
}

/// <summary>
/// Timers' running state, shared by every timer widget with the same key and kept while widgets are rebuilt (a layout
/// switch, a page swipe, an edit). Lost when the app closes. Only the UI thread uses it.
/// </summary>
public static class TimerClock
{
    private static readonly Dictionary<string, State> States = new();

    public static State For(string key)
    {
        if (!States.TryGetValue(key, out var state))
            States[key] = state = new State();
        return state;
    }

    public sealed class State
    {
        private TimeSpan _banked;
        private DateTime? _startedAt;
        private bool _announced;
        private DateTime? _anchor;

        public bool Running => _startedAt is not null;

        public TimeSpan Elapsed(DateTime now) => _banked + (_startedAt is { } at ? now - at : TimeSpan.Zero);

        public void Toggle(DateTime now)
        {
            if (_startedAt is { } at)
            {
                _banked += now - at;
                _startedAt = null;
            }
            else
                _startedAt = now;
        }

        public void Reset(DateTime now)
        {
            _banked = TimeSpan.Zero;
            _startedAt = null;
            _announced = false;
            _anchor = now;
        }

        /// <summary>True the first time it's asked after reaching zero, so one finish is announced once.</summary>
        public bool Announce()
        {
            if (_announced)
                return false;
            _announced = true;
            return true;
        }

        /// <summary>
        /// When an "until" timer started looking for its next time of day: fixed, so the target doesn't jump to
        /// tomorrow the moment today's passes.
        /// </summary>
        public DateTime Anchor(DateTime now) => _anchor ??= now;
    }
}
