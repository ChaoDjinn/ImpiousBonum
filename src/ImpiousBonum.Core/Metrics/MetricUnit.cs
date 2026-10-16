namespace ImpiousBonum.Core.Metrics;

public enum MetricUnit
{
    None,
    Percent,
    Bytes,
    BytesPerSecond,
    Celsius,
    Milliseconds,
    FramesPerSecond,
    Text,
    Megahertz,
    Rpm,
    Volts,
    Watts,
    Fahrenheit,
    KilometresPerHour,
    MilesPerHour,
    Hectopascals,
    InchesOfMercury,
    Millimetres,
    Inches,
    Kilometres,
    Miles,

    /// <summary>An angle, such as a wind direction or how high the sun is.</summary>
    Degrees,

    /// <summary>µg/m³: how much of a gas or particles is in the air.</summary>
    MicrogramsPerCubicMetre,

    /// <summary>Pollen grains in a cubic metre of air.</summary>
    GrainsPerCubicMetre,

    /// <summary>Energy: a kilowatt for an hour, as electricity is billed.</summary>
    KilowattHours,

    /// <summary>gCO₂/kWh: how much carbon dioxide making a kWh of electricity gives off.</summary>
    GramsOfCo2PerKilowattHour,
}
