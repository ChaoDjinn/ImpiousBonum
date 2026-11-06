using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class AveragedHistoryTests
{
    // On a 10-second boundary, and on the hour, so every step lines up.
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);

    private static (MetricStore Store, FakeTime Time) Store()
    {
        var time = new FakeTime(Start);
        var store = new MetricStore(time: time);
        store.Register(new MetricDefinition("cpu.load", "CPU load", "CPU", MetricUnit.Percent, 100));
        return (store, time);
    }

    /// <summary>Sets one reading a second, the way most providers do, starting now.</summary>
    private static void Readings(MetricStore store, FakeTime time, params double?[] values)
    {
        foreach (var value in values)
        {
            store.Set("cpu.load", value);
            time.Now += TimeSpan.FromSeconds(1);
        }
    }

    [Fact]
    public void Nothing_is_kept_until_a_graph_asks_and_then_it_fills_from_then_on()
    {
        var (store, time) = Store();
        Readings(store, time, 50, 50, 50);

        var history = store.GetHistory("cpu.load", LongSpan.Hour);
        Assert.Empty(history.Average);
        Assert.Equal(TimeSpan.FromSeconds(10), history.Step);

        Readings(store, time, 10, 30);
        history = store.GetHistory("cpu.load", LongSpan.Hour);
        Assert.Equal([20.0], history.Average);
        Assert.Equal([10.0], history.Low);
        Assert.Equal([30.0], history.High);
    }

    [Fact]
    public void Each_step_is_the_average_of_its_readings_with_the_lowest_and_highest()
    {
        var (store, time) = Store();
        store.GetHistory("cpu.load", LongSpan.Hour);

        // One step of 10 readings, a spike among them, then the next step under way.
        Readings(store, time, 10, 10, 10, 10, 90, 10, 10, 10, 10, 10);
        Readings(store, time, 40, 60);

        var history = store.GetHistory("cpu.load", LongSpan.Hour);
        Assert.Equal([18.0, 50.0], history.Average);
        Assert.Equal([10.0, 40.0], history.Low);
        Assert.Equal([90.0, 60.0], history.High);
    }

    [Fact]
    public void Steps_without_readings_are_gaps_and_an_empty_step_under_way_is_left_off()
    {
        var (store, time) = Store();
        store.GetHistory("cpu.load", LongSpan.Hour);
        Readings(store, time, 20, 20);

        // No reading for the rest of that step, a whole step, and into the next; then no readings (null) at all.
        time.Now = Start + TimeSpan.FromSeconds(25);
        Readings(store, time, 40);
        time.Now = Start + TimeSpan.FromSeconds(30);
        Readings(store, time, null, null);

        var history = store.GetHistory("cpu.load", LongSpan.Hour);
        Assert.Equal([20.0, double.NaN, 40.0], history.Average);
        Assert.Equal([20.0, double.NaN, 40.0], history.High);
    }

    [Fact]
    public void A_span_shows_its_own_number_of_steps()
    {
        var (store, time) = Store();
        store.GetHistory("cpu.load", LongSpan.FifteenMinutes);
        store.GetHistory("cpu.load", LongSpan.Day);

        // Twenty minutes, a reading every 10 seconds.
        for (var i = 0; i < 120; i++)
        {
            store.Set("cpu.load", i);
            time.Now += TimeSpan.FromSeconds(10);
        }

        var quarter = store.GetHistory("cpu.load", LongSpan.FifteenMinutes);
        Assert.Equal(90, quarter.Average.Length);
        Assert.Equal(119, quarter.Average[^1]);
        // An hour's worth, from the same steps, is the twenty minutes there are.
        Assert.Equal(120, store.GetHistory("cpu.load", LongSpan.Hour).Average.Length);

        var day = store.GetHistory("cpu.load", LongSpan.Day);
        Assert.Equal(TimeSpan.FromMinutes(5), day.Step);
        Assert.Equal([14.5, 44.5, 74.5, 104.5], day.Average);
        Assert.Equal([0.0, 30.0, 60.0, 90.0], day.Low);
    }

    [Fact]
    public void A_long_sleep_leaves_at_most_a_ringful_of_gap()
    {
        var (store, time) = Store();
        store.GetHistory("cpu.load", LongSpan.Hour);
        Readings(store, time, 30);

        time.Now += TimeSpan.FromDays(3);
        Readings(store, time, 70);

        var history = store.GetHistory("cpu.load", LongSpan.Hour);
        Assert.Equal(360, history.Average.Length);
        Assert.Equal(70, history.Average[^1]);
        Assert.All(history.Average[..^1], v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void An_unknown_metric_has_none()
    {
        var (store, _) = Store();

        Assert.Empty(store.GetHistory("gpu.load", LongSpan.SixHours).Average);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
