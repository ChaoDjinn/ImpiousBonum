using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Tests;

public sealed class DemoReadingsTests
{
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);

    /// <summary>The screenshots in the README and the guide use these, so a reading they lack shows as a dash there.</summary>
    [Fact]
    public void The_layouts_in_the_screenshots_have_every_reading_they_show()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening, [1, 2, 3]);

        var layouts = new List<(string Name, LayoutDocument Layout)> { ("default", LayoutStore.LoadDefault()) };
        layouts.AddRange(LayoutsIn("gallery"));
        layouts.AddRange(LayoutsIn(Path.Combine("build", "screenshots")));
        layouts.AddRange(LayoutsIn("build"));

        var missing = layouts
            .SelectMany(l => l.Layout.Widgets.SelectMany(MetricsIn).Select(id => (l.Name, Id: id)))
            .Where(m => !store.TryGet(m.Id, out var sample) || !sample.HasValue)
            .Select(m => $"{m.Name}: {m.Id}")
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    /// <summary>The layouts the widget pictures and the social preview are drawn from (build/screenshots.ps1).</summary>
    [Fact]
    public void The_picture_layouts_are_valid()
    {
        var layouts = LayoutsIn(Path.Combine("build", "screenshots")).Concat(LayoutsIn("build")).ToList();
        Assert.NotEmpty(layouts);
        foreach (var (name, layout) in layouts)
            Assert.True(LayoutValidator.Validate(layout) is { Count: 0 }, $"{name}: {string.Join(" ", LayoutValidator.Validate(layout))}");
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
    public void Every_weather_reading_has_a_made_up_value()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening);

        var weather = store.Definitions.Where(d => d.Category == "Weather").ToList();
        Assert.Contains(weather, d => d.Id == WeatherProvider.SunElevation);
        Assert.All(weather, d => Assert.True(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));
        store.TryGet(WeatherProvider.PressureTrend, out var trend);
        Assert.Equal("Falling", trend.Text);

        // As long a forecast as Open-Meteo's, so the widget can be shown at its longest.
        var forecast = (WeatherReport)store.GetAttachment(WeatherProvider.Forecast)!;
        Assert.Equal(48, forecast.HoursAhead.Count());
        Assert.Equal(16, forecast.Days.Count);
    }

    [Fact]
    public void Every_air_quality_and_pollen_reading_has_a_made_up_value()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening);

        var air = store.Definitions.Where(d => d.Category == "Air quality").ToList();
        Assert.Contains(air, d => d.Id == AirQualityProvider.WeedLevel);
        Assert.All(air, d => Assert.True(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));
        store.TryGet(AirQualityProvider.Level, out var level);
        Assert.Equal("Fair", level.Text);
        store.TryGet(AirQualityProvider.Pollen, out var pollen);
        Assert.Equal("Grass low", pollen.Text);
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

    /// <summary>The layout files in a folder of the repository, found by walking up from the test's output folder.</summary>
    private static List<(string Name, LayoutDocument Layout)> LayoutsIn(string folder)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ImpiousBonum.slnx")))
            {
                return [.. Directory.GetFiles(Path.Combine(dir.FullName, folder), "*.json")
                    .Where(f => !f.EndsWith("index.json"))
                    .Select(f => (Path.GetFileName(f), JsonSerializer.Deserialize<LayoutDocument>(File.ReadAllText(f), JsonDefaults.Options)!))];
            }
        }
        throw new DirectoryNotFoundException($"The repository's {folder} folder");
    }
}
