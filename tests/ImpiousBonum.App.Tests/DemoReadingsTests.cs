using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class DemoReadingsTests
{
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);

    /// <summary>The screenshots in the README use these, so a reading they lack shows as a dash there.</summary>
    [Fact]
    public void The_default_and_gallery_layouts_have_every_reading_they_show()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening, [1, 2, 3]);

        var layouts = new List<(string Name, LayoutDocument Layout)> { ("default", LayoutStore.LoadDefault()) };
        foreach (var file in Directory.GetFiles(GalleryFolder(), "*.json").Where(f => !f.EndsWith("index.json")))
            layouts.Add((Path.GetFileName(file), JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(file), JsonDefaults.Options)!));

        var missing = layouts
            .SelectMany(l => l.Layout.Widgets.SelectMany(MetricsIn).Select(id => (l.Name, Id: id)))
            .Where(m => !store.TryGet(m.Id, out var sample) || !sample.HasValue)
            .Select(m => $"{m.Name}: {m.Id}")
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Graphs_have_a_full_history_and_the_extras_are_there()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening, [1, 2, 3]);

        Assert.DoesNotContain(store.GetHistory(CpuProvider.Load, MetricStore.DefaultHistoryLength), double.IsNaN);
        Assert.Equal(MetricStore.DefaultHistoryLength, store.GetHistory(CpuProvider.Load, MetricStore.DefaultHistoryLength).Length);
        Assert.NotEmpty((IReadOnlyList<AppUsage>)store.GetAttachment(ProcessProvider.Apps)!);
        Assert.NotNull(store.GetAttachment(WeatherProvider.Forecast));
        Assert.NotEmpty((IReadOnlyList<Core.Calendar.CalendarEvent>)store.GetAttachment(CalendarProvider.Events)!);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.GetAttachment(MediaProvider.Artwork));
    }

    [Fact]
    public void They_are_the_same_every_time()
    {
        var (first, second) = (new MetricStore(), new MetricStore());
        DemoReadings.Fill(first, Evening);
        DemoReadings.Fill(second, Evening);
        Assert.Equal(first.GetHistory(CpuProvider.Load, 240), second.GetHistory(CpuProvider.Load, 240));
    }

    [Fact]
    public void The_demo_album_cover_is_a_picture() =>
        Sta.Run(() => Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, DemoArtwork.Png(64)[..4]));

    /// <summary>Metric ids a widget shows: its metric settings and the placeholders in its text, at any depth.</summary>
    private static IEnumerable<string> MetricsIn(JsonObject widget)
    {
        // The drives widget's templates have per-drive placeholders such as {free}, which aren't metric ids.
        if (widget.GetString("type") == "drives")
            yield break;
        foreach (var (key, node) in Walk(widget))
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var text))
                continue;
            if (key == "metric" && text.Length > 0)
                yield return text;
            else if (text.Contains('{'))
                foreach (var id in ValueTemplate.Parse(text).MetricIds)
                    yield return id;
        }
    }

    private static IEnumerable<(string Key, JsonNode? Node)> Walk(JsonNode? node, string key = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, child) in obj)
                    foreach (var inner in Walk(child, k))
                        yield return inner;
                break;
            case JsonArray array:
                foreach (var child in array)
                    foreach (var inner in Walk(child, key))
                        yield return inner;
                break;
            default:
                yield return (key, node);
                break;
        }
    }

    private static string GalleryFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "gallery")) && File.Exists(Path.Combine(dir.FullName, "ImpiousBonum.slnx")))
                return Path.Combine(dir.FullName, "gallery");
        }
        throw new DirectoryNotFoundException("The repository's gallery folder");
    }
}
