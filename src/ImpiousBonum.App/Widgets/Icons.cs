using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>Stroked line icons on a 24×24 grid, drawn in the accent colour.</summary>
public static class Icons
{
    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.OrdinalIgnoreCase)
    {
        // Chip with pins on every side.
        ["cpu"] = Parse("M7,5 H17 A2,2 0 0 1 19,7 V17 A2,2 0 0 1 17,19 H7 A2,2 0 0 1 5,17 V7 A2,2 0 0 1 7,5 Z " +
                        "M9.5,9.5 H14.5 V14.5 H9.5 Z " +
                        "M9,2 V5 M12,2 V5 M15,2 V5 M9,19 V22 M12,19 V22 M15,19 V22 " +
                        "M2,9 H5 M2,12 H5 M2,15 H5 M19,9 H22 M19,12 H22 M19,15 H22"),
        // Graphics card: bracket, board, fan, edge connector.
        ["gpu"] = Parse("M1.5,5 H4 V21 " +
                        "M4,7 H21 A1.5,1.5 0 0 1 22.5,8.5 V15.5 A1.5,1.5 0 0 1 21,17 H4 " +
                        "M16,9.5 A2.5,2.5 0 1 1 15.99,9.5 Z " +
                        "M7,17 V19.5 H13 V17"),
        // Memory stick.
        ["ram"] = Parse("M2,7 H22 V16 H2 Z M6,10 V13 M10,10 V13 M14,10 V13 M18,10 V13 M5,16 V19 M9,16 V19 M15,16 V19 M19,16 V19"),
        // Hard drive.
        ["disk"] = Parse("M3,14 L6,5 H18 L21,14 V19 H3 Z M3,14 H21 M17,16.5 H17.01"),
        // Up/down arrows.
        ["network"] = Parse("M8,20 V4 M4,8 L8,4 L12,8 M16,4 V20 M12,16 L16,20 L20,16"),
        // Gauge.
        ["fps"] = Parse("M4,17 A9,9 0 1 1 20,17 M12,14 L16,8"),
    };

    public static Geometry? Get(string? name) => name is not null && Geometries.TryGetValue(name, out var geometry) ? geometry : null;

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
