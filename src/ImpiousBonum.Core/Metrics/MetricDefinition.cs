namespace ImpiousBonum.Core.Metrics;

/// <summary>Describes a single value the app can display, e.g. <c>cpu.load</c>.</summary>
/// <param name="Id">Stable dotted identifier used by layouts, e.g. <c>disk.C.free</c>.</param>
/// <param name="Name">Human readable name shown in pickers.</param>
/// <param name="Category">Grouping for pickers, e.g. "CPU", "Disk".</param>
/// <param name="Unit">Controls formatting.</param>
/// <param name="Max">Known upper bound for graphs and bars, if any.</param>
public sealed record MetricDefinition(string Id, string Name, string Category, MetricUnit Unit, double? Max = null);
