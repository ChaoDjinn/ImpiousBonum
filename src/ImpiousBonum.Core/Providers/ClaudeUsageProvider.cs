using ImpiousBonum.Core.Claude;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Claude plan usage (the 5-hour session and weekly limits) as reported by Claude Code's status line through
/// <see cref="ClaudeUsageFile"/>. The values have no reading unless Claude Code is open and has reported limits, so
/// widgets that hide without a reading only show while it can actually update them.
/// </summary>
public sealed class ClaudeUsageProvider(string usagePath, TimeProvider? time = null) : IMetricProvider
{
    public const string Session = "claude.session";
    public const string SessionResets = "claude.session.resets";
    public const string Week = "claude.week";
    public const string WeekResets = "claude.week.resets";
    public const string Status = "claude.status";

    /// <summary>The <see cref="Status"/> text while Claude Code is open and reporting limits.</summary>
    public const string Connected = "Claude Code connected";

    /// <summary>
    /// How long after the status line last ran Claude Code still counts as open. The status line is set up to run every
    /// 15 seconds, so this allows a few missed runs.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Session, "Claude session (5-hour) usage", "Claude", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(SessionResets, "Claude session resets", "Claude", MetricUnit.Text));
        store.Register(new MetricDefinition(Week, "Claude weekly usage", "Claude", MetricUnit.Percent, 100));
        store.Register(new MetricDefinition(WeekResets, "Claude week resets", "Claude", MetricUnit.Text));
        store.Register(new MetricDefinition(Status, "Claude Code connection", "Claude", MetricUnit.Text));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var snapshot = ClaudeUsageFile.Read(usagePath);
        var age = snapshot is null ? (TimeSpan?)null : now - DateTimeOffset.FromUnixTimeSeconds(snapshot.Updated);

        string status;
        if (snapshot is null)
            status = "Not connected to Claude Code";
        else if (age > StaleAfter)
            status = "Claude Code isn't open";
        else if (!snapshot.HasLimits)
            status = "Waiting for Claude Code's first reply";
        else
            status = Connected;

        var active = snapshot is not null && age <= StaleAfter && snapshot.HasLimits;
        Publish(store, Session, SessionResets, active ? snapshot!.Reading(snapshot.FiveHour, now) : default);
        Publish(store, Week, WeekResets, active ? snapshot!.Reading(snapshot.SevenDay, now) : default);
        store.SetText(Status, status);
        return ValueTask.CompletedTask;
    }

    private static void Publish(MetricStore store, string usedId, string resetsId, ClaudeUsageReading reading)
    {
        store.Set(usedId, reading.UsedPercentage);
        store.SetText(resetsId, reading.ResetsText);
    }

    public void Dispose()
    {
    }
}
