using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class MetricStoreTests
{
    [Fact]
    public void History_returns_most_recent_values_oldest_first()
    {
        var store = new MetricStore(historyLength: 3);
        store.Register(new MetricDefinition("m", "m", "m", MetricUnit.None));
        foreach (var value in new double[] { 1, 2, 3, 4, 5 })
            store.Set("m", value);

        Assert.Equal([3, 4, 5], store.GetHistory("m", 10));
        Assert.Equal([4, 5], store.GetHistory("m", 2));
    }

    [Fact]
    public void Missing_readings_are_recorded_as_gaps()
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("m", "m", "m", MetricUnit.None));
        store.Set("m", 1);
        store.Set("m", null);

        var history = store.GetHistory("m", 2);
        Assert.Equal(1, history[0]);
        Assert.True(double.IsNaN(history[1]));
    }

    [Fact]
    public void Writes_to_unregistered_metrics_are_ignored()
    {
        var store = new MetricStore();
        store.Set("nope", 1);
        Assert.False(store.TryGet("nope", out _));
    }

    [Fact]
    public void Re_registering_keeps_the_current_value()
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("m", "old", "m", MetricUnit.None));
        store.Set("m", 7);
        store.Register(new MetricDefinition("m", "new", "m", MetricUnit.None, Max: 10));

        Assert.True(store.TryGet("m", out var sample));
        Assert.Equal(7, sample.Value);
        Assert.Equal(10, sample.Definition.Max);
    }
}
