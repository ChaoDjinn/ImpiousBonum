using System.Text.Json.Nodes;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// The saved design: a fixed-size canvas (scaled to fit the window), a theme, and positioned widgets.
/// Widgets stay as raw JSON objects so each widget type can own its own settings.
/// </summary>
public sealed class LayoutDocument
{
    /// <summary>Design width in layout units. The canvas is scaled uniformly to fill the window.</summary>
    public double Width { get; set; } = 1920;

    public double Height { get; set; } = 480;

    public ThemeSettings Theme { get; set; } = new();

    /// <summary>Each widget has <c>type</c>, <c>x</c>, <c>y</c>, <c>width</c>, <c>height</c> plus type-specific settings.</summary>
    public List<JsonObject> Widgets { get; set; } = [];
}

public sealed class ThemeSettings
{
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Optional path to a .ttf/.otf file to use instead of an installed font. The family name is read from the file.</summary>
    public string? FontFile { get; set; }

    public string FontWeight { get; set; } = "Light";

    public string Foreground { get; set; } = "#FFFFFF";

    /// <summary>Used for less important text such as the values in widget headers.</summary>
    public string Secondary { get; set; } = "#D0FFFFFF";

    public string Accent { get; set; } = "#FF9800";

    public string Background { get; set; } = "#000000";

    /// <summary>Optional path to a PNG/JPG drawn over <see cref="Background"/>, behind the widgets.</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>How the image covers the canvas: fill, fit, stretch, center or tile.</summary>
    public string BackgroundFit { get; set; } = "fill";

    /// <summary>0–1. Lower values let the background colour show through the image.</summary>
    public double BackgroundOpacity { get; set; } = 1;

    /// <summary>Unfilled part of bars.</summary>
    public string Track { get; set; } = "#1A1A1A";

    /// <summary>Named by warning colour rules as <c>warning</c>.</summary>
    public string Warning { get; set; } = "#FFC107";

    /// <summary>Named by warning colour rules as <c>critical</c>.</summary>
    public string Critical { get; set; } = "#F44336";
}
