using System.Diagnostics;
using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The PC's power, the energy it's used and what that costs. The power is the CPU's package power and the graphics
/// card's from the sensor service, plus an allowance for everything they leave out (the motherboard, memory, drives and
/// fans); without the sensor service there's neither, so there's no power reading and nothing is counted. It's added up
/// into kWh today and this month (see <see cref="EnergyMeter"/>), only while the app runs, and with a price per kWh the
/// costs are those at that price. The totals are kept in a small file, so a restart carries on from them.
/// </summary>
/// <param name="settings">The allowance, the price and the currency. Read on every sample, so changes apply at once.</param>
/// <param name="totalsPath">Where today's and this month's totals are kept between runs; null keeps them only while it runs.</param>
/// <param name="grid">
/// The grid's carbon intensity, sampled on this loop every <see cref="GridEvery"/> without holding it up: it's off by
/// default, and a loop of its own would wake whether it's on or not.
/// </param>
public sealed class EnergyProvider(
    Func<EnergySettings> settings,
    string? totalsPath = null,
    GridCarbonProvider? grid = null,
    TimeProvider? time = null) : IMetricProvider
{
    public const string Power = "power.total";
    public const string Today = "energy.today";
    public const string Month = "energy.month";
    public const string CostToday = "energy.cost.today";
    public const string CostMonth = "energy.cost.month";

    private const string Category = "Energy";

    /// <summary>How often the totals are saved while they change: a run that's stopped without closing (Windows shutting down, say) loses at most this.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    /// <summary>How often the grid's carbon intensity is sampled: its readings change every half-hour.</summary>
    public static readonly TimeSpan GridEvery = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly EnergyMeter _meter = new((time ?? TimeProvider.System).LocalTimeZone);
    private EnergyTotals? _saved;
    private DateTimeOffset _savedAt;
    private string? _currency;
    private DateTimeOffset _nextGrid;
    private Task? _gridSample;

    /// <summary>In step with the sensor service's readings, so <see cref="Power"/>'s graph keeps time with theirs.</summary>
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Power, "Power: the whole PC, CPU and graphics card measured and the rest estimated", Category, MetricUnit.Watts, Format: "0"));
        store.Register(new MetricDefinition(Today, "Energy used today", Category, MetricUnit.KilowattHours));
        store.Register(new MetricDefinition(Month, "Energy used this month", Category, MetricUnit.KilowattHours, Format: "0.0"));
        RegisterCosts(store, settings().Currency);
        grid?.Initialize(store);
        if (totalsPath is not null)
        {
            _saved = EnergyTotals.Read(totalsPath);
            _meter.Restore(_saved);
        }
    }

    /// <summary>The costs show in the chosen currency, which can change while it runs.</summary>
    private void RegisterCosts(MetricStore store, string currency)
    {
        if (currency == _currency)
            return;
        _currency = currency;
        var format = EnergyCost.Format(currency);
        store.Register(new MetricDefinition(CostToday, "Energy cost today", Category, MetricUnit.None, Format: format));
        store.Register(new MetricDefinition(CostMonth, "Energy cost this month", Category, MetricUnit.None, Format: format));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var wanted = settings();
        var power = TotalPower(Reading(store, SensorAliases.CpuPower), Reading(store, SensorAliases.GpuPower), wanted.OtherWatts);
        _meter.Add(now, power);
        Publish(store, power, _meter.Totals, wanted);

        if (totalsPath is not null && _meter.Totals is { } totals && totals != _saved && now - _savedAt >= SaveEvery)
        {
            EnergyTotals.Write(totalsPath, totals);
            (_saved, _savedAt) = (totals, now);
        }

        if (grid is not null && now >= _nextGrid && _gridSample is not { IsCompleted: false })
        {
            Observe(_gridSample);
            _nextGrid = now + GridEvery;
            // Not awaited: a slow answer from the web mustn't hold up counting the energy.
            _gridSample = grid.SampleAsync(store, cancellationToken).AsTask();
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The whole PC: whichever of the CPU's and graphics card's power the sensor service reads (the CPU's needs the PawnIO
    /// driver, and some graphics don't report theirs), plus the allowance for the rest. Null with neither.
    /// </summary>
    public static double? TotalPower(double? cpu, double? gpu, double otherWatts) =>
        cpu is null && gpu is null ? null : (cpu ?? 0) + (gpu ?? 0) + Math.Max(0, otherWatts);

    /// <param name="totals">What's been counted, already moved on to today.</param>
    internal void Publish(MetricStore store, double? power, EnergyTotals? totals, EnergySettings wanted)
    {
        RegisterCosts(store, wanted.Currency);
        store.Set(Power, power);
        store.Set(Today, totals?.Today);
        store.Set(Month, totals?.Month);
        store.Set(CostToday, EnergyCost.Of(totals?.Today, wanted.Price));
        store.Set(CostMonth, EnergyCost.Of(totals?.Month, wanted.Price));
    }

    private static double? Reading(MetricStore store, string id) =>
        store.TryGet(id, out var sample) && sample.Value is double value && double.IsFinite(value) ? value : null;

    /// <summary>A grid sample that went wrong in a way it doesn't expect is noted, as the sampler would have.</summary>
    private static void Observe(Task? sample)
    {
        if (sample?.Exception is { } failed)
            Trace.TraceWarning($"{nameof(GridCarbonProvider)} sample failed: {failed.InnerException?.Message}");
    }

    /// <summary>Keeps what's been counted since the last save.</summary>
    public void Dispose()
    {
        if (totalsPath is not null && _meter.Totals is { } totals && totals != _saved)
            EnergyTotals.Write(totalsPath, totals);
    }
}
