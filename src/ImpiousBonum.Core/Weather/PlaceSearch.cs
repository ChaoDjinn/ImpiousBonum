using System.Globalization;

namespace ImpiousBonum.Core.Weather;

/// <summary>What a place search found, and whether OpenStreetMap was asked (its places need crediting when shown).</summary>
public sealed record PlaceSearchResults(IReadOnlyList<WeatherPlace> Places, bool FromOpenStreetMap);

/// <summary>
/// Finds places for the weather by name. Open-Meteo is asked first: for "West Derby, Liverpool" it's asked for West Derby,
/// and only the ones in Liverpool are kept, since it can't take the comma itself. When it has nothing (or nothing there),
/// OpenStreetMap's place search is asked instead, which knows suburbs, parks and postcodes. Nothing is sent until someone
/// searches.
/// </summary>
public static class PlaceSearch
{
    /// <summary>Most places shown for a search.</summary>
    public const int MostShown = 10;

    /// <summary>How many places to ask Open-Meteo for when only those in an area will do: as many as it gives.</summary>
    private const int MostToNarrowDown = 100;

    /// <summary>What people write for some countries, as their codes.</summary>
    private static readonly Dictionary<string, string> CountryShortNames = new(StringComparer.OrdinalIgnoreCase) { ["UK"] = "GB", ["USA"] = "US" };

    public static Task<PlaceSearchResults> SearchAsync(string query, CancellationToken cancellationToken) =>
        SearchAsync(query, OpenMeteo.SearchAsync, OpenStreetMap.SearchAsync, cancellationToken);

    /// <param name="openMeteo">Searches Open-Meteo for a name, for up to so many places; tests swap it.</param>
    /// <param name="openStreetMap">Searches OpenStreetMap for the whole query; tests swap it.</param>
    internal static async Task<PlaceSearchResults> SearchAsync(
        string query,
        Func<string, int, CancellationToken, Task<IReadOnlyList<WeatherPlace>>> openMeteo,
        Func<string, CancellationToken, Task<IReadOnlyList<WeatherPlace>>> openStreetMap,
        CancellationToken cancellationToken)
    {
        var (name, areas) = Split(query);
        if (name.Length < 2)
            return new([], false);

        var found = await openMeteo(name, areas.Count > 0 ? MostToNarrowDown : MostShown, cancellationToken).ConfigureAwait(false);
        // Open-Meteo's search is fuzzy, so "London, UK" finds London Colney and Londonderry too: London itself comes first.
        var places = Within(found.OrderBy(p => !IsCalled(p, name)), areas);
        if (places.Count > 0)
            return new(places, false);

        // OpenStreetMap reads "West Derby, Liverpool" and postcodes itself, so it gets the query as typed.
        var more = await openStreetMap(query.Trim(), cancellationToken).ConfigureAwait(false);
        return new([.. more.Take(MostShown)], true);
    }

    /// <summary>"West Derby, Liverpool, UK": the place's name, then the areas it should be in.</summary>
    internal static (string Name, IReadOnlyList<string> Areas) Split(string query)
    {
        var parts = query.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ("", []) : (parts[0], parts[1..]);
    }

    /// <summary>
    /// The places in every one of <paramref name="areas"/>, in the order given, at most <see cref="MostShown"/>. An area can
    /// be a town or county, a region, a country or its code (UK and USA too). The start of a word is enough ("Liv", or
    /// "Bavaria" for Upper Bavaria), and case and accents don't matter.
    /// </summary>
    internal static IReadOnlyList<WeatherPlace> Within(IEnumerable<WeatherPlace> places, IReadOnlyList<string> areas) =>
        [.. places.Where(p => areas.All(a => IsIn(p, a))).Take(MostShown)];

    private static bool IsCalled(WeatherPlace place, string name) =>
        string.Compare(place.Name, name, CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    private static bool IsIn(WeatherPlace place, string area)
    {
        if (place.Areas.Append(place.Region).Append(place.Country).Any(part => part is not null && StartsAWord(part, area)))
            return true;
        var code = CountryShortNames.GetValueOrDefault(area, area);
        return string.Equals(place.CountryCode, code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsAWord(string text, string start)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if ((i == 0 || !char.IsLetterOrDigit(text[i - 1]))
                && CultureInfo.InvariantCulture.CompareInfo.IsPrefix(text.AsSpan(i), start, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
                return true;
        }
        return false;
    }
}
