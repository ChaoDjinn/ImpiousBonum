using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImpiousBonum.Core.Energy;

/// <summary>
/// The energy counted on <paramref name="Day"/> and in its month so far, in kWh: what <c>energy.json</c> in the settings
/// folder keeps between runs, so a restart carries on from today's and this month's totals.
/// </summary>
public sealed record EnergyTotals(DateOnly Day, double Today, double Month)
{
    public const string FileName = "energy.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>The totals kept at <paramref name="path"/>, or null when there are none or they can't be read. Never throws.</summary>
    public static EnergyTotals? Read(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var totals = JsonSerializer.Deserialize<EnergyTotals>(File.ReadAllText(path), Options);
            // A hand edit gone wrong counts as nothing kept, rather than a negative month.
            return totals is { Today: >= 0, Month: >= 0 } && double.IsFinite(totals.Today) && double.IsFinite(totals.Month) ? totals : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes to a temporary file and moves it into place, so a run that stops halfway never leaves half a file. A file
    /// that can't be written is left for the next save.
    /// </summary>
    public static void Write(string path, EnergyTotals totals)
    {
        var temp = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(totals, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
