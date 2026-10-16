using System.Globalization;

namespace ImpiousBonum.Core.Energy;

/// <summary>What the energy readings need from the settings.</summary>
/// <param name="OtherWatts">An allowance for the parts the sensor service doesn't measure: the motherboard, memory, drives and fans.</param>
/// <param name="Price">What a kWh costs, or null for no cost readings.</param>
/// <param name="Currency">The currency symbol the costs show, e.g. £.</param>
public sealed record EnergySettings(double OtherWatts, double? Price, string Currency)
{
    /// <summary>About what a desktop's motherboard, memory, a couple of drives and its fans use between them.</summary>
    public const double DefaultOtherWatts = 50;

    public static EnergySettings Default => new(DefaultOtherWatts, null, EnergyCost.DefaultSymbol);
}

/// <summary>Prices as they're typed in, and costs as the region writes money.</summary>
public static class EnergyCost
{
    /// <summary>The currency symbol of the region Windows is set to, e.g. £.</summary>
    public static string DefaultSymbol => NumberFormatInfo.CurrentInfo.CurrencySymbol;

    /// <summary>
    /// A price per kWh as it's typed: <c>0.25</c> (or <c>0,25</c>), or in pence or cents with a p or c after it
    /// (<c>24.5p</c>), with the currency symbol in front if it's there. Null if it isn't a price more than 0.
    /// </summary>
    public static double? ParsePrice(string? text, string? symbol = null)
    {
        var price = (text ?? "").Trim();
        if (symbol is { Length: > 0 } && price.StartsWith(symbol, StringComparison.OrdinalIgnoreCase))
            price = price[symbol.Length..].TrimStart();
        var hundredths = price.Length > 0 && price[^1] is 'p' or 'P' or 'c' or 'C' or '¢';
        if (hundredths)
            price = price[..^1].TrimEnd();
        if (!TryNumber(price, out var value))
            return null;
        if (hundredths)
            value /= 100;
        // Nowhere charges a thousand of anything for a kWh: that's a typing slip.
        return value is > 0 and < 1000 ? value : null;
    }

    /// <summary>
    /// The number format for a cost in <paramref name="symbol"/>, with the symbol where the region puts it and the
    /// region's decimals: <c>£0.48</c> in the UK, <c>0,48 €</c> in Germany. The symbol is escaped, so anything typed
    /// shows as it is.
    /// </summary>
    public static string Format(string symbol, NumberFormatInfo? region = null)
    {
        region ??= NumberFormatInfo.CurrentInfo;
        var escaped = string.Concat(symbol.Select(c => $"\\{c}"));
        var number = region.CurrencyDecimalDigits > 0 ? "0." + new string('0', region.CurrencyDecimalDigits) : "0";
        return region.CurrencyPositivePattern switch
        {
            1 => number + escaped,
            2 => $"{escaped}\\ {number}",
            3 => $"{number}\\ {escaped}",
            _ => escaped + number,
        };
    }

    /// <summary>What <paramref name="kilowattHours"/> cost at <paramref name="price"/>, or null without either.</summary>
    public static double? Of(double? kilowattHours, double? price) => kilowattHours * price;

    /// <summary>In the region's way of writing numbers, or with a dot or a comma for the decimals whichever the region uses.</summary>
    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
