using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ImpiousBonum.App.Layout;

/// <summary>What moves over a theme's background: falling snow, leaves, petals or confetti, or rising embers.</summary>
public enum ParticleKind
{
    None,
    Snow,
    Leaves,
    Petals,
    Confetti,
    Embers,
}

/// <summary>One snowflake, leaf, petal, scrap of confetti or ember.</summary>
internal sealed class Particle
{
    /// <summary>Its centre, in canvas units.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>0 for the farthest, 1 for the nearest: nearer ones are bigger, faster and brighter.</summary>
    public double Depth { get; set; }

    /// <summary>A snowflake's or ember's radius, or the length of a leaf, petal or scrap of confetti.</summary>
    public double Size { get; set; }

    /// <summary>A leaf's, petal's or scrap's width as a share of its length.</summary>
    public double Aspect { get; set; }

    /// <summary>How fast it falls, in canvas units a second. An ember's is below zero, as it rises.</summary>
    public double Fall { get; set; }

    /// <summary>How far it swings to each side as it goes.</summary>
    public double Sway { get; set; }

    /// <summary>Where it is in its swing, in radians, and how fast that goes.</summary>
    public double Phase { get; set; }

    public double PhaseSpeed { get; set; }

    /// <summary>Which way it points, in degrees, and how fast it turns.</summary>
    public double Angle { get; set; }

    public double Spin { get; set; }

    /// <summary>Tumbling over: its width goes with the cosine of this. For an ember, its flicker.</summary>
    public double Flip { get; set; }

    public double FlipSpeed { get; set; }

    /// <summary>An ember's age, and how long it glows, in seconds.</summary>
    public double Age { get; set; }

    public double Life { get; set; }

    /// <summary>Which of the kind's colours it is.</summary>
    public int Shade { get; set; }
}

/// <summary>
/// Where each particle is and how it moves, kept apart from the drawing so it can be tested. Each has a depth that sets
/// its size, speed and brightness, so they don't move as one; a breeze that rises and falls carries them all a little.
/// </summary>
internal sealed class ParticleField
{
    /// <summary>The most there can be, however big the canvas: plenty for a flurry, few enough to draw cheaply.</summary>
    public const int MaxCount = 150;

    /// <summary>A step longer than this (after a pause, or a slow frame) counts as this, so nothing jumps.</summary>
    public const double LongestStep = 0.1;

    private readonly Random _random;
    private readonly Particle[] _particles;
    private readonly int _shades;
    private double _time;

    /// <param name="shades">How many colours the kind has, for <see cref="Particle.Shade"/>.</param>
    /// <param name="seed">The same seed places them the same way, so a still frame doesn't change from one picture to the next.</param>
    public ParticleField(ParticleKind kind, double width, double height, double amount, int shades, int seed = 1)
    {
        Kind = kind;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Scale = SizeScale(width, height);
        _shades = Math.Max(1, shades);
        _random = new Random(seed);
        _particles = new Particle[Count(kind, width, height, amount)];
        for (var i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new Particle();
            Spawn(_particles[i], anywhere: true);
        }
    }

    public ParticleKind Kind { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>How much bigger than on a 1920-wide canvas everything is and moves, so a 4K canvas doesn't get specks.</summary>
    public double Scale { get; }

    public IReadOnlyList<Particle> Particles => _particles;

    /// <summary>How far past the left and right edges one can drift before it comes back on the other side.</summary>
    public double Margin => 40 * Scale;

    /// <summary>The breeze now, in canvas units a second: mostly one way, now and then the other, changing over half a minute or so.</summary>
    public double Wind => (0.25 + 0.6 * Math.Sin(_time * 0.21) + 0.3 * Math.Sin(_time * 0.53 + 1.3)) * WindStrength * Scale;

    private double WindStrength => Kind switch
    {
        ParticleKind.Leaves => 30,
        ParticleKind.Petals => 22,
        ParticleKind.Snow => 12,
        ParticleKind.Embers => 10,
        _ => 6,
    };

    public static double SizeScale(double width, double height) => Math.Clamp(Math.Max(width, height) / 1920, 0.6, 2);

    /// <summary>
    /// How many for a canvas of this size: a few dozen on a 1920 × 480 strip at 0.5, about as many on a 4K canvas as
    /// on a 1080p one (since they're drawn bigger there), and fewer of the bigger kinds.
    /// </summary>
    public static int Count(ParticleKind kind, double width, double height, double amount)
    {
        if (kind == ParticleKind.None || !(amount > 0) || !(width > 0) || !(height > 0))
            return 0;
        var scale = SizeScale(width, height);
        // At full amount, per million square units of a 1920-wide canvas.
        var density = kind switch
        {
            ParticleKind.Snow => 90,
            ParticleKind.Confetti => 60,
            ParticleKind.Embers => 60,
            ParticleKind.Petals => 40,
            _ => 30,
        };
        var count = Math.Min(amount, 1) * density * width * height / (scale * scale) / 1_000_000;
        return Math.Clamp((int)Math.Round(count), 4, MaxCount);
    }

    /// <summary>How far from its centre <paramref name="p"/> is drawn: a flake's and an ember's soft glow reach past their size.</summary>
    public double Reach(Particle p) => Kind switch
    {
        ParticleKind.Snow => p.Size * 1.6,
        ParticleKind.Embers => p.Size * 4,
        _ => p.Size * 0.6,
    };

    /// <summary>How opaque <paramref name="p"/> is now, 0–1: farther ones fainter, and embers flicker and fade as they cool.</summary>
    public double Opacity(Particle p) => Kind switch
    {
        ParticleKind.Snow => 0.4 + 0.55 * p.Depth,
        ParticleKind.Leaves => 0.65 + 0.35 * p.Depth,
        ParticleKind.Petals => 0.6 + 0.4 * p.Depth,
        ParticleKind.Confetti => 0.75 + 0.25 * p.Depth,
        ParticleKind.Embers => Glow(p),
        _ => 0,
    };

    private static double Glow(Particle p)
    {
        var age = p.Life > 0 ? Math.Clamp(p.Age / p.Life, 0, 1) : 1;
        // Flares up quickly, stays bright a while, then fades out.
        var fade = Math.Min(1, age * 10) * (1 - age * age);
        return fade * (0.75 + 0.25 * Math.Sin(p.Flip)) * (0.7 + 0.3 * p.Depth);
    }

    /// <summary>Moves everything on by <paramref name="seconds"/>, bringing back what has gone off the canvas.</summary>
    public void Step(double seconds)
    {
        var dt = Math.Clamp(seconds, 0, LongestStep);
        _time += dt;
        var wind = Wind;
        foreach (var p in _particles)
        {
            p.Phase += p.PhaseSpeed * dt;
            p.Flip += p.FlipSpeed * dt;
            p.Angle = (p.Angle + p.Spin * dt) % 360;
            p.Age += dt;
            // Nearer ones feel the breeze more, and each swings from side to side on its own.
            p.X += (wind * (0.5 + 0.5 * p.Depth) + p.Sway * p.PhaseSpeed * Math.Cos(p.Phase)) * dt;
            // Leaves and petals glide: they drop fastest through the middle of each swing and hang at its ends.
            var glide = Kind is ParticleKind.Leaves or ParticleKind.Petals ? 0.45 + 0.55 * Math.Abs(Math.Cos(p.Phase)) : 1;
            p.Y += p.Fall * glide * dt;

            if (Gone(p))
                Spawn(p, anywhere: false);
            else if (p.X < -Margin)
                p.X += Width + 2 * Margin;
            else if (p.X > Width + Margin)
                p.X -= Width + 2 * Margin;
        }
    }

    private bool Gone(Particle p) => Kind == ParticleKind.Embers
        ? p.Age >= p.Life || p.Y < -Reach(p)
        : p.Y > Height + Reach(p);

    /// <summary>Starts <paramref name="p"/> again: just above the top (embers just below the bottom), or anywhere at first.</summary>
    private void Spawn(Particle p, bool anywhere)
    {
        double Between(double low, double high) => low + (high - low) * _random.NextDouble();
        double Either() => _random.Next(2) == 0 ? -1 : 1;

        var d = p.Depth = _random.NextDouble();
        var jitter = Between(0.85, 1.15) * Scale;
        p.Shade = _random.Next(_shades);
        p.Phase = Between(0, 2 * Math.PI);
        p.Flip = Between(0, 2 * Math.PI);
        p.Angle = Between(0, 360);
        p.Age = 0;
        p.Life = 0;
        p.Spin = 0;
        p.FlipSpeed = 0;
        switch (Kind)
        {
            case ParticleKind.Snow:
                p.Size = (1.8 + 4.8 * d * d) * jitter;
                p.Fall = (16 + 40 * d) * jitter;
                p.Sway = (4 + 14 * d) * Scale;
                p.PhaseSpeed = Between(0.5, 1.4);
                break;
            case ParticleKind.Leaves:
                p.Size = (24 + 22 * d) * jitter;
                p.Aspect = Between(0.5, 0.68);
                p.Fall = (26 + 34 * d) * jitter;
                p.Sway = (14 + 26 * d) * Scale;
                p.PhaseSpeed = Between(0.9, 1.7);
                p.Spin = Between(25, 90) * Either();
                p.FlipSpeed = Between(1.2, 3.2);
                break;
            case ParticleKind.Petals:
                p.Size = (13 + 11 * d) * jitter;
                p.Aspect = Between(0.7, 0.85);
                p.Fall = (14 + 24 * d) * jitter;
                p.Sway = (12 + 20 * d) * Scale;
                p.PhaseSpeed = Between(0.7, 1.4);
                p.Spin = Between(15, 70) * Either();
                p.FlipSpeed = Between(0.8, 2.4);
                break;
            case ParticleKind.Confetti:
                p.Size = (11 + 8 * d) * jitter;
                p.Aspect = Between(0.35, 0.7);
                p.Fall = (38 + 46 * d) * jitter;
                p.Sway = (3 + 9 * d) * Scale;
                p.PhaseSpeed = Between(1.2, 3);
                p.Spin = Between(60, 260) * Either();
                p.FlipSpeed = Between(3.5, 9);
                break;
            case ParticleKind.Embers:
                p.Size = (1.2 + 2.2 * d) * jitter;
                p.Fall = -(22 + 40 * d) * jitter;
                p.Sway = (6 + 14 * d) * Scale;
                p.PhaseSpeed = Between(0.9, 2.4);
                p.FlipSpeed = Between(7, 15);
                p.Life = Between(3, 7);
                break;
        }

        p.X = Between(-Margin, Width + Margin);
        if (Kind == ParticleKind.Embers)
        {
            p.Y = Height + Reach(p);
            if (anywhere)
            {
                // Part-way through its life, risen as far as it would have by now.
                p.Age = Between(0, p.Life);
                p.Y += p.Fall * p.Age;
            }
        }
        else
            p.Y = anywhere ? Between(-Reach(p), Height + Reach(p)) : -Reach(p);
    }
}

/// <summary>
/// A theme's particles: snow, autumn leaves, cherry-blossom petals or confetti falling over the background and behind
/// the widgets, or embers rising. One element draws them all, 20 to 25 times a second, and only while the dashboard
/// is showing; copies that are never on screen (the tablet view, a USB screen, a snapshot) get a still frame and no timer.
/// </summary>
public sealed class ParticleLayer : FrameworkElement
{
    /// <summary>The <c>particles</c> theme setting's values.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["none", "snow", "leaves", "petals", "confetti", "embers"];

    /// <summary>
    /// 25 frames a second, which Windows' timer usually makes about 21: smooth enough for something drifting, and each
    /// frame redraws the whole dashboard.
    /// </summary>
    public static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(40);

    /// <summary>How many steps of opacity brushes are made for; finer would look no different.</summary>
    private const int Levels = 6;

    private static readonly Geometry LeafShape = Frozen("M 0,-0.5 C 0.5,-0.38 0.8,0 0.02,0.38 C -0.72,0.02 -0.48,-0.36 0,-0.5 Z");

    private static readonly Geometry LeafVeins = Frozen(
        "M 0.05,0.56 L 0.02,0.38 L 0,-0.42 M 0.01,0.12 L 0.3,-0.06 M 0.01,-0.08 L 0.26,-0.26 M 0.01,0.04 L -0.3,-0.12 M 0,-0.16 L -0.22,-0.32");

    private static readonly Geometry PetalShape = Frozen(
        "M 0,0.5 C -0.3,0.38 -0.55,0.02 -0.44,-0.3 C -0.38,-0.48 -0.18,-0.54 -0.06,-0.44 L 0,-0.36 L 0.06,-0.44 C 0.18,-0.54 0.38,-0.48 0.44,-0.3 C 0.55,0.02 0.3,0.38 0,0.5 Z");

    private static readonly Rect Scrap = new(-0.5, -0.5, 1, 1);

    private readonly ParticleField _field;
    private readonly IReadOnlyList<Color> _colors;
    private readonly Brush?[] _brushes;
    private readonly Pen?[] _veins;
    private readonly DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();

    private ParticleLayer(ParticleKind kind, IReadOnlyList<Color> colors, double width, double height, double amount, bool animate)
    {
        _colors = colors;
        _field = new ParticleField(kind, width, height, amount, colors.Count);
        _brushes = new Brush?[colors.Count * (Levels + 1) * 2];
        _veins = new Pen?[colors.Count * (Levels + 1)];
        Width = width;
        Height = height;
        IsHitTestVisible = false;
        Focusable = false;
        // Snapped to whole pixels, slow ones would move in jerks.
        SnapsToDevicePixels = false;
        if (animate)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = FrameTime };
            _timer.Tick += (_, _) => Frame();
        }
        // Taken off the dashboard by someone else (the tablet view letting its widgets go, say): stop with it.
        Unloaded += (_, _) => Pause();
    }

    /// <summary>The theme's particles for a canvas of this size, or null when it has none.</summary>
    /// <param name="animate">False for a copy that's never on screen: a still frame, with no timer.</param>
    public static ParticleLayer? Create(ThemeSettings theme, double width, double height, bool animate)
    {
        var kind = Parse(theme.Particles);
        if (ParticleField.Count(kind, width, height, theme.ParticleAmount) == 0)
            return null;
        return new ParticleLayer(kind, Palette(kind, theme), width, height, theme.ParticleAmount, animate);
    }

    public static ParticleKind Parse(string? value) => value switch
    {
        "snow" => ParticleKind.Snow,
        "leaves" => ParticleKind.Leaves,
        "petals" => ParticleKind.Petals,
        "confetti" => ParticleKind.Confetti,
        "embers" => ParticleKind.Embers,
        _ => ParticleKind.None,
    };

    public ParticleKind Kind => _field.Kind;

    /// <summary>True while they're moving.</summary>
    public bool Playing => _timer?.IsEnabled == true;

    internal ParticleField Field => _field;

    public void Play()
    {
        if (_timer is null || _timer.IsEnabled)
            return;
        _clock.Restart();
        _timer.Start();
    }

    public void Pause() => _timer?.Stop();

    /// <summary>
    /// The colours they come in: <c>particleColor</c> in a few shades if the theme sets one, confetti in the theme's own
    /// colours, and otherwise the kind's: white snow, autumn leaves, pink petals, orange and gold embers.
    /// </summary>
    internal static IReadOnlyList<Color> Palette(ParticleKind kind, ThemeSettings theme)
    {
        if (ParseColor(theme.ParticleColor) is { } own)
            return [own, Mix(own, Colors.White, 0.3), Mix(own, Colors.Black, 0.2)];
        return kind switch
        {
            ParticleKind.Snow => [Colors.White, Rgb(0xF2F7FF), Rgb(0xE3EEFF)],
            ParticleKind.Leaves => [Rgb(0xE8671B), Rgb(0xF29A2E), Rgb(0xC8401E), Rgb(0xD9A21B), Rgb(0xA24B1C), Rgb(0xB8321F)],
            ParticleKind.Petals => [Rgb(0xFFC2D4), Rgb(0xFFB0C8), Rgb(0xFAD3DE), Rgb(0xF79DBF), Rgb(0xFFE0EA)],
            ParticleKind.Embers => [Rgb(0xFFB238), Rgb(0xFF8A1F), Rgb(0xFFD36B), Rgb(0xFF6A13)],
            _ => Accents(theme),
        };
    }

    /// <summary>Confetti in the theme's colours: its accents, warning and critical colours and its text.</summary>
    private static IReadOnlyList<Color> Accents(ThemeSettings theme)
    {
        var colors = new[] { theme.Accent, theme.Accent2, theme.Warning, theme.Critical, theme.Foreground }
            .Select(ParseColor)
            .OfType<Color>()
            .Where(c => c.A > 0)
            .Select(c => Color.FromRgb(c.R, c.G, c.B))
            .Distinct()
            .ToList();
        return colors.Count > 0 ? colors : [Colors.Orange];
    }

    private void Frame()
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        _field.Step(elapsed);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var kind = _field.Kind;
        foreach (var p in _field.Particles)
        {
            var level = (int)Math.Round(_field.Opacity(p) * Levels);
            if (level <= 0)
                continue;
            if (kind is ParticleKind.Snow or ParticleKind.Embers)
            {
                var radius = _field.Reach(p);
                dc.DrawEllipse(Brush(p.Shade, level, back: false), null, new Point(p.X, p.Y), radius, radius);
                continue;
            }

            // Tumbling: the shape narrows as it turns edge-on, and shows its darker back as it turns over.
            // Confetti spins right round; leaves and petals only rock, so they never quite turn edge-on.
            var turn = Math.Cos(p.Flip);
            var thinnest = kind == ParticleKind.Confetti ? 0.1 : 0.3;
            var narrow = thinnest + (1 - thinnest) * Math.Abs(turn);
            var place = Matrix.Identity;
            if (kind == ParticleKind.Confetti)
                place.Scale(p.Size * p.Aspect, p.Size * narrow);
            else
                place.Scale(p.Size * p.Aspect * narrow, p.Size);
            // Leaves and petals rock with their swing as well as turning.
            place.Rotate(kind == ParticleKind.Confetti ? p.Angle : p.Angle + 30 * Math.Sin(p.Phase));
            place.Translate(p.X, p.Y);

            dc.PushTransform(new MatrixTransform(place));
            var fill = Brush(p.Shade, level, back: turn < 0);
            switch (kind)
            {
                case ParticleKind.Leaves:
                    dc.DrawGeometry(fill, null, LeafShape);
                    dc.DrawGeometry(null, Vein(p.Shade, level), LeafVeins);
                    break;
                case ParticleKind.Petals:
                    dc.DrawGeometry(fill, null, PetalShape);
                    break;
                default:
                    dc.DrawRectangle(fill, null, Scrap);
                    break;
            }
            dc.Pop();
        }
    }

    /// <summary>The brush for a shade at an opacity, made the first time it's needed and kept.</summary>
    private Brush Brush(int shade, int level, bool back)
    {
        var index = (shade * (Levels + 1) + level) * 2 + (back ? 1 : 0);
        if (_brushes[index] is { } made)
            return made;

        var color = _colors[shade];
        var alpha = (double)level / Levels;
        Brush brush = _field.Kind switch
        {
            // Soft round flakes and glowing embers: bright in the middle, fading to nothing at the edge.
            ParticleKind.Snow => Radial(
                (0, With(color, alpha)), (0.45, With(color, alpha * 0.8)), (1, With(color, 0))),
            ParticleKind.Embers => Radial(
                (0, With(Mix(color, Colors.White, 0.55), alpha)), (0.22, With(color, alpha)), (0.5, With(color, alpha * 0.35)), (1, With(color, 0))),
            // Shaded from the tip to the stalk; the underside is duller.
            ParticleKind.Leaves => Linear(
                With(Mix(color, back ? Colors.Black : Colors.White, back ? 0.2 : 0.18), alpha), With(Mix(color, Colors.Black, back ? 0.4 : 0.25), alpha)),
            // Pale at the notched tip, deeper pink where it joined the flower.
            ParticleKind.Petals => Linear(
                With(Mix(color, Colors.White, back ? 0.2 : 0.4), alpha), With(Mix(color, Rgb(0xE8709A), back ? 0.2 : 0.35), alpha)),
            _ => new SolidColorBrush(With(back ? Mix(color, Colors.Black, 0.3) : color, alpha)),
        };
        brush.Freeze();
        return _brushes[index] = brush;
    }

    private Pen Vein(int shade, int level)
    {
        var index = shade * (Levels + 1) + level;
        if (_veins[index] is { } made)
            return made;
        var pen = new Pen(new SolidColorBrush(With(Mix(_colors[shade], Colors.Black, 0.45), 0.6 * level / Levels)), 0.035)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return _veins[index] = pen;
    }

    private static RadialGradientBrush Radial(params (double Offset, Color Color)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    /// <summary>From <paramref name="tip"/> at the top of the shape to <paramref name="stalk"/> at the bottom.</summary>
    private static LinearGradientBrush Linear(Color tip, Color stalk) => new(tip, stalk, 90);

    private static Geometry Frozen(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Color With(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * color.A), color.R, color.G, color.B);

    private static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(from.A, Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    private static Color? ParseColor(string? value)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? null : (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
