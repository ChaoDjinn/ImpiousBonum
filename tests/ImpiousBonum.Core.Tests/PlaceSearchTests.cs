using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class PlaceSearchTests
{
    // Trimmed from real Open-Meteo place searches for West Derby, Schwabing, London, Springfield and Croxteth.
    private const string OpenMeteoPlaces = """
        {"results":[
          {"name":"West Derby","latitude":53.43279,"longitude":-2.90963,"feature_code":"PPL","country_code":"GB","country":"United Kingdom","admin1":"England","admin2":"Liverpool"},
          {"name":"West Derby","latitude":-27.42654,"longitude":23.56428,"feature_code":"PPL","country_code":"ZA","country":"South Africa","admin1":"North West"},
          {"name":"Schwabing","latitude":48.16,"longitude":11.58,"country_code":"DE","country":"Germany","admin1":"Bavaria","admin2":"Upper Bavaria","admin3":"Munich, Urban District","admin4":"Munich"},
          {"name":"London","latitude":51.50853,"longitude":-0.12574,"country_code":"GB","country":"United Kingdom","admin1":"England","admin2":"Greater London"},
          {"name":"Springfield","latitude":42.10148,"longitude":-72.58981,"country_code":"US","country":"United States","admin1":"Massachusetts","admin2":"Hampden","admin3":"City of Springfield"},
          {"name":"Croxteth","latitude":-30.95485,"longitude":26.82305,"country_code":"ZA","country":"South Africa","admin1":"Eastern Cape","admin2":"Joe Gqabi District Municipality","admin3":"Maletswai"}
        ]}
        """;

    // OpenStreetMap's place search (Nominatim, format=jsonv2&addressdetails=1) for "West Derby, Liverpool" and "L12 5HJ".
    private const string OpenStreetMapPlaces = """
        [{"lat":"53.4325964","lon":"-2.9095592","category":"place","type":"suburb","addresstype":"suburb","name":"West Derby",
          "display_name":"West Derby, Liverpool, Liverpool City Region, England, L12 5HJ, United Kingdom",
          "address":{"suburb":"West Derby","city":"Liverpool","state_district":"Liverpool City Region","state":"England","postcode":"L12 5HJ","country":"United Kingdom","country_code":"gb"}},
         {"lat":"53.4326248","lon":"-2.9100000","category":"place","type":"postcode","addresstype":"postcode","name":"",
          "display_name":"L12 5HJ, Liverpool, Liverpool City Region, England, United Kingdom",
          "address":{"postcode":"L12 5HJ","city":"Liverpool","state_district":"Liverpool City Region","state":"England","country":"United Kingdom","country_code":"gb"}},
         {"display_name":"Nowhere without coordinates"}]
        """;

    [Fact]
    public void Open_Meteo_places_say_which_town_and_county_they_are_in()
    {
        var places = OpenMeteo.ParsePlaces(OpenMeteoPlaces);

        Assert.Equal(
            [
                "West Derby, Liverpool, England, United Kingdom",
                "West Derby, North West, South Africa",
                "Schwabing, Munich, Upper Bavaria, Bavaria, Germany",
                "London, England, United Kingdom",
                "Springfield, Hampden, Massachusetts, United States",
                "Croxteth, Joe Gqabi District Municipality, Eastern Cape, South Africa",
            ],
            places.Select(p => p.Description));
        Assert.Equal(["Liverpool"], places[0].Areas);
        Assert.Equal("GB", places[0].CountryCode);
        Assert.Equal((53.43279, -2.90963), (places[0].Latitude, places[0].Longitude));
    }

    [Fact]
    public void Descriptions_leave_out_empty_and_repeated_parts()
    {
        static string Describe(string name, string? region, string? country, params string[] areas) =>
            new WeatherPlace(name, region, country, 0, 0) { Areas = areas }.Description;

        Assert.Equal("Springfield, Missouri, United States", Describe("Springfield", "Missouri", "United States"));
        Assert.Equal("Liverpool, England, United Kingdom", Describe("Liverpool", "England", "United Kingdom", "liverpool"));
        Assert.Equal("Gràcia, Barcelona, Catalonia, Spain", Describe("Gràcia", "Catalonia", "Spain", "Barcelona", " ", "Barcelona"));

        // An area that only names a part before it again adds nothing; one that merely starts the same does.
        Assert.Equal("West Derby, Liverpool, England, United Kingdom", Describe("West Derby", "England", "United Kingdom", "West Derby", "Liverpool", "Liverpool City Region"));
        Assert.Equal("York, North Yorkshire, England, United Kingdom", Describe("York", "England", "United Kingdom", "North Yorkshire"));

        // A region or country goes only when it's the same as a part before it.
        Assert.Equal("New York, United States", Describe("New York", "New York", "United States"));
        Assert.Equal("York, Livingston, New York, United States", Describe("York", "New York", "United States", "Livingston"));
        Assert.Equal("Monaco", Describe("Monaco", null, "Monaco"));
    }

    [Theory]
    [InlineData("West Derby", "West Derby", new string[0])]
    [InlineData(" West Derby , Liverpool ", "West Derby", new[] { "Liverpool" })]
    [InlineData("West Derby,, Liverpool, UK,", "West Derby", new[] { "Liverpool", "UK" })]
    [InlineData(" , ", "", new string[0])]
    public void A_search_is_split_into_the_place_and_where_it_is(string query, string name, string[] areas)
    {
        var split = PlaceSearch.Split(query);
        Assert.Equal(name, split.Name);
        Assert.Equal(areas, split.Areas);
    }

    [Theory]
    [InlineData("Liverpool", "West Derby, Liverpool, England, United Kingdom")]
    [InlineData("liv", "West Derby, Liverpool, England, United Kingdom")]
    [InlineData("England", "West Derby, Liverpool, England, United Kingdom")]
    [InlineData("United Kingdom", "West Derby, Liverpool, England, United Kingdom")]
    [InlineData("GB", "West Derby, Liverpool, England, United Kingdom")]
    [InlineData("UK", "West Derby, Liverpool, England, United Kingdom")]
    [InlineData("South Africa", "West Derby, North West, South Africa")]
    [InlineData("West", "West Derby, North West, South Africa")]
    public void The_area_after_the_comma_picks_the_right_place(string area, string expected)
    {
        var westDerbys = OpenMeteo.ParsePlaces(OpenMeteoPlaces).Where(p => p.Name == "West Derby").ToList();
        Assert.Equal(expected, Assert.Single(PlaceSearch.Within(westDerbys, [area])).Description);
    }

    [Fact]
    public void Every_area_named_has_to_match_and_the_order_is_kept()
    {
        var places = OpenMeteo.ParsePlaces(OpenMeteoPlaces);

        Assert.Equal(places.Count, PlaceSearch.Within(places, []).Count);
        Assert.Equal(PlaceSearch.MostShown, PlaceSearch.Within(Enumerable.Repeat(places[0], 25), []).Count);
        Assert.Equal(["West Derby", "London"], PlaceSearch.Within(places, ["England"]).Select(p => p.Name));
        Assert.Equal("London", Assert.Single(PlaceSearch.Within(places, ["England", "Greater"])).Name);
        Assert.Empty(PlaceSearch.Within(places, ["Liverpool", "South Africa"]));

        // The start of any word will do ("Bavaria" for Upper Bavaria too), whatever the case or accents.
        Assert.Equal("Schwabing", Assert.Single(PlaceSearch.Within(places, ["munich", "bavaria"])).Name);
        var paris = new WeatherPlace("Paris", "Île-de-France", "France", 48.85, 2.35);
        Assert.Single(PlaceSearch.Within([paris], ["ile-de-france"]));
        Assert.Empty(PlaceSearch.Within([paris], ["France-Comté"]));
        Assert.Empty(PlaceSearch.Within([paris], ["rance"]));
    }

    [Fact]
    public void OpenStreetMap_places_read_their_coordinates_and_address()
    {
        var places = OpenStreetMap.ParsePlaces(OpenStreetMapPlaces);

        Assert.Equal(2, places.Count);
        var westDerby = places[0];
        Assert.Equal("West Derby, Liverpool, England, United Kingdom", westDerby.Description);
        Assert.Equal((53.4325964, -2.9095592), (westDerby.Latitude, westDerby.Longitude));
        Assert.Equal(["West Derby", "Liverpool", "Liverpool City Region"], westDerby.Areas);
        Assert.Equal("GB", westDerby.CountryCode);

        // A postcode has no name of its own, so it's called by the start of its full name.
        Assert.Equal("L12 5HJ, Liverpool, England, United Kingdom", places[1].Description);

        // A county is shown when the address has one, before the state.
        var village = OpenStreetMap.ParsePlaces("""
            [{"lat":"53.9","lon":"-2.4","name":"Downham","address":{"village":"Downham","county":"Lancashire","state":"England","country":"United Kingdom"}}]
            """);
        Assert.Equal("Downham, Lancashire, England, United Kingdom", Assert.Single(village).Description);

        Assert.Empty(OpenStreetMap.ParsePlaces("[]"));
        Assert.Empty(OpenStreetMap.ParsePlaces("""{"error":"Unable to geocode"}"""));
    }

    [Fact]
    public async Task OpenStreetMap_is_only_asked_when_Open_Meteo_has_nothing_there()
    {
        var openStreetMapPlaces = OpenStreetMap.ParsePlaces(OpenStreetMapPlaces);
        var asked = new List<string>();

        Task<PlaceSearchResults> Search(string query) => PlaceSearch.SearchAsync(
            query,
            (name, count, _) =>
            {
                asked.Add($"Open-Meteo: {name} ({count})");
                return Task.FromResult<IReadOnlyList<WeatherPlace>>([.. OpenMeteo.ParsePlaces(OpenMeteoPlaces).Where(p => p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))]);
            },
            (whole, _) =>
            {
                asked.Add($"OpenStreetMap: {whole}");
                return Task.FromResult(openStreetMapPlaces);
            },
            CancellationToken.None);

        // Open-Meteo has West Derby, so OpenStreetMap isn't asked.
        var found = await Search("West Derby");
        Assert.Equal(["Open-Meteo: West Derby (10)"], asked);
        Assert.False(found.FromOpenStreetMap);
        Assert.Equal(2, found.Places.Count);

        // "West Derby, Liverpool": Open-Meteo is asked for plenty of West Derbys, and the one in Liverpool kept.
        asked.Clear();
        found = await Search("West Derby, Liverpool");
        Assert.Equal(["Open-Meteo: West Derby (100)"], asked);
        Assert.Equal("West Derby, Liverpool, England, United Kingdom", Assert.Single(found.Places).Description);

        // Open-Meteo's only Croxteth is in South Africa: OpenStreetMap gets the whole search.
        asked.Clear();
        found = await Search("Croxteth, Liverpool");
        Assert.Equal(["Open-Meteo: Croxteth (100)", "OpenStreetMap: Croxteth, Liverpool"], asked);
        Assert.True(found.FromOpenStreetMap);
        Assert.Equal(openStreetMapPlaces, found.Places);

        // Open-Meteo knows no parks or postcodes.
        asked.Clear();
        found = await Search("Croxteth Park");
        Assert.Equal(["Open-Meteo: Croxteth Park (10)", "OpenStreetMap: Croxteth Park"], asked);
        asked.Clear();
        found = await Search(" L12 5HJ ");
        Assert.Equal(["Open-Meteo: L12 5HJ (10)", "OpenStreetMap: L12 5HJ"], asked);
        Assert.True(found.FromOpenStreetMap);

        // Too short to search for: nothing is sent.
        asked.Clear();
        found = await Search("L, Liverpool");
        Assert.Empty(asked);
        Assert.Empty(found.Places);
    }

    [Fact]
    public async Task A_place_called_exactly_what_was_searched_for_comes_first()
    {
        // Open-Meteo's search is fuzzy: in its order, London Colney came before London.
        WeatherPlace[] found =
        [
            new("London Colney", "England", "United Kingdom", 51.7, -0.3) { CountryCode = "GB" },
            new("Londonderry", "Northern Ireland", "United Kingdom", 55.0, -7.3) { CountryCode = "GB" },
            new("London", "England", "United Kingdom", 51.5, -0.13) { CountryCode = "GB" },
            new("London", "Ontario", "Canada", 43.0, -81.2) { CountryCode = "CA" },
        ];

        var result = await PlaceSearch.SearchAsync(
            "london, UK",
            (_, _, _) => Task.FromResult<IReadOnlyList<WeatherPlace>>(found),
            (_, _) => throw new InvalidOperationException("OpenStreetMap shouldn't be asked"),
            CancellationToken.None);

        Assert.Equal(["London", "London Colney", "Londonderry"], result.Places.Select(p => p.Name));
    }

    [Fact]
    public void OpenStreetMap_searches_are_kept_a_second_apart()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        var spacing = new RequestSpacing(TimeSpan.FromSeconds(1), time);

        Assert.Equal(TimeSpan.Zero, spacing.Reserve());
        Assert.Equal(TimeSpan.FromSeconds(1), spacing.Reserve());
        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(TimeSpan.FromMilliseconds(1600), spacing.Reserve());
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, spacing.Reserve());
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
