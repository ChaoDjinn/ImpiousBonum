using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Pictures of the widgets for the <see cref="AddWidgetWindow"/>: each widget as it starts, at its starting size, showing
/// the made-up readings the docs' screenshots use (see <see cref="App.FillDemo"/>), in the layout's theme. Each is drawn
/// once off-screen and kept for that theme, so nothing on it ticks or listens: the audio visualiser shows the demo's
/// moment of music rather than what's playing.
/// </summary>
internal static class WidgetPreviews
{
    /// <summary>How many pictures are kept before they're all let go: every widget in a few themes.</summary>
    private const int MostKept = 120;

    private static readonly Dictionary<(string Theme, string Type), ImageSource> Kept = [];
    private static MetricStore? _demo;

    /// <summary>
    /// Settings a widget is shown with where it starts with nothing to show: an image widget starts without a picture, so
    /// it shows the game in front's cover instead, and a rows widget without rows, so it shows the ones its description
    /// gives as an example.
    /// </summary>
    private static readonly Dictionary<string, JsonObject> ShownWith = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = new JsonObject { ["source"] = "game" },
        ["rows"] = new JsonObject
        {
            ["rows"] = new JsonArray(
                new JsonObject { ["label"] = "Download", ["text"] = "{net.down}" },
                new JsonObject { ["label"] = "Upload", ["text"] = "{net.up}" },
                new JsonObject { ["label"] = "Ping", ["text"] = "{net.ping}" }),
        },
    };

    /// <summary>The demo's moment, a quarter to ten at night, as the screenshots have it.</summary>
    private static DateTime Moment => DateTime.Today.AddHours(21).AddMinutes(47);

    /// <summary>The widget as it starts, in <paramref name="themeSettings"/>, as a picture of its starting size; kept for next time.</summary>
    public static ImageSource Draw(WidgetDescriptor descriptor, ThemeSettings themeSettings)
    {
        var key = (themeSettings.ToJson().ToJsonString(), descriptor.Type);
        if (Kept.TryGetValue(key, out var kept))
            return kept;

        var width = Math.Max(1, descriptor.DefaultWidth);
        var height = Math.Max(1, descriptor.DefaultHeight);
        var theme = Theme.From(themeSettings);
        var widget = WidgetFactory.Create(StartingDefinition(descriptor, width, height), theme);
        widget.Width = width;
        widget.Height = height;
        widget.Effect = theme.Glow;
        var canvas = new Grid { Width = width, Height = height, Background = theme.Background, ClipToBounds = true };
        canvas.Children.Add(widget);

        widget.Refresh(Demo(), Moment);
        var size = new Size(width, height);
        canvas.Measure(size);
        canvas.Arrange(new Rect(size));
        canvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        bitmap.Freeze();
        if (Kept.Count >= MostKept)
            Kept.Clear();
        Kept[key] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// The widget's layout entry as the editor adds it (with any lists it starts with filled in), and the settings it's
    /// shown with here.
    /// </summary>
    internal static JsonObject StartingDefinition(WidgetDescriptor descriptor, double width, double height)
    {
        var definition = new JsonObject { ["type"] = descriptor.Type, ["width"] = width, ["height"] = height };
        foreach (var items in descriptor.Settings.Where(s => s.Default is JsonArray))
            definition[items.Key] = ((JsonArray)items.Default!).DeepClone();
        if (ShownWith.TryGetValue(descriptor.Type, out var shown))
        {
            foreach (var (name, value) in shown)
                definition[name] = value?.DeepClone();
        }
        return definition;
    }

    private static MetricStore Demo()
    {
        if (_demo is null)
        {
            _demo = new MetricStore();
            App.FillDemo(_demo, Moment);
        }
        return _demo;
    }
}
