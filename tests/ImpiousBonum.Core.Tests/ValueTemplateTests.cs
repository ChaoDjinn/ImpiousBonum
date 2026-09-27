using System.Globalization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class ValueTemplateTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
    private readonly MetricStore _store = new();

    public ValueTemplateTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        _store.Register(new MetricDefinition("cpu.load", "CPU", "CPU", MetricUnit.Percent));
        _store.Register(new MetricDefinition("cpu.threads", "Threads", "CPU", MetricUnit.None));
        _store.Register(new MetricDefinition("cpu.temp", "Temp", "CPU", MetricUnit.Celsius));
        _store.Set("cpu.load", 11.8);
        _store.Set("cpu.threads", 16);
    }

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    [Fact]
    public void Replaces_placeholders_and_keeps_literals() =>
        Assert.Equal("11.8 % / 16 threads", ValueTemplate.Parse("{cpu.load} / {cpu.threads} threads").Render(_store));

    [Fact]
    public void Applies_format_spec() =>
        Assert.Equal("12", ValueTemplate.Parse("{cpu.load:0 nounit}").Render(_store));

    [Fact]
    public void Unreadable_and_unknown_metrics_show_a_dash() =>
        Assert.Equal("— °C —", ValueTemplate.Parse("{cpu.temp:nounit} °C {nope}").Render(_store));

    [Fact]
    public void Double_braces_are_literal() =>
        Assert.Equal("{cpu.load}", ValueTemplate.Parse("{{cpu.load}}").Render(_store));

    [Fact]
    public void Lists_referenced_metrics() =>
        Assert.Equal(["cpu.load", "cpu.threads"], ValueTemplate.Parse("{cpu.load} / {cpu.threads:N0}").MetricIds);
}
