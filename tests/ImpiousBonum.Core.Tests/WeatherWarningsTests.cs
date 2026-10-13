using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Tests;

public sealed class WeatherWarningsTests
{
    // The NWS's answer for Lake Charles, Louisiana (30.25, -93.2) on the morning of 13 October 2026, trimmed: two flood
    // warnings for the Calcasieu River, each an update of an earlier one, with the long texts cut short.
    private const string LakeCharles = """
        {
            "@context": ["https://geojson.org/geojson-ld/geojson-context.jsonld", {"@version": "1.1", "wx": "https://api.weather.gov/ontology#", "@vocab": "https://api.weather.gov/ontology#"}],
            "type": "FeatureCollection",
            "features": [
                {
                    "id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.002.1",
                    "type": "Feature",
                    "geometry": {"type": "Polygon", "coordinates": [[[-93.14, 30.38], [-93.03, 30.31], [-93.08, 30.25], [-93.21, 30.19], [-93.29, 30.3], [-93.14, 30.38]]]},
                    "properties": {
                        "@id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.002.1",
                        "@type": "wx:Alert",
                        "id": "urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.002.1",
                        "areaDesc": "Calcasieu, LA",
                        "geocode": {"SAME": ["022019"], "UGC": ["LAC019"]},
                        "affectedZones": ["https://api.weather.gov/zones/county/LAC019"],
                        "references": [
                            {
                                "@id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.001.1",
                                "identifier": "urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.001.1",
                                "sender": "w-nws.webmaster@noaa.gov",
                                "sent": "2026-10-12T10:35:00-05:00"
                            }
                        ],
                        "sent": "2026-10-12T21:21:00-05:00",
                        "effective": "2026-10-12T21:21:00-05:00",
                        "onset": "2026-10-12T21:21:00-05:00",
                        "expires": "2026-10-13T09:30:00-05:00",
                        "ends": "2026-10-15T13:00:00-05:00",
                        "status": "Actual",
                        "messageType": "Update",
                        "category": "Met",
                        "severity": "Severe",
                        "certainty": "Observed",
                        "urgency": "Immediate",
                        "event": "Flood Warning",
                        "sender": "w-nws.webmaster@noaa.gov",
                        "senderName": "NWS Lake Charles LA",
                        "headline": "Flood Warning issued October 12 at 9:21PM CDT until October 15 at 1:00PM CDT by NWS Lake Charles LA",
                        "description": "...The Flood Warning continues for the following rivers in\nLouisiana...\n\nCalcasieu River near White Oak Park affecting Calcasieu Parish.\n\n* WHAT...Minor flooding is forecast.",
                        "instruction": "Turn around, don't drown when encountering flooded roads. Most flood\ndeaths occur in vehicles.",
                        "response": "Avoid",
                        "parameters": {"NWSheadline": ["FLOOD WARNING NOW IN EFFECT UNTIL THURSDAY AFTERNOON"], "VTEC": ["/O.EXT.KLCH.FL.W.0046.000000T0000Z-261015T1800Z/"]}
                    }
                },
                {
                    "id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.003.1",
                    "type": "Feature",
                    "geometry": null,
                    "properties": {
                        "@id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.003.1",
                        "@type": "wx:Alert",
                        "id": "urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.003.1",
                        "areaDesc": "Calcasieu, LA",
                        "references": [
                            {
                                "@id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.002.1",
                                "identifier": "urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.002.1",
                                "sender": "w-nws.webmaster@noaa.gov",
                                "sent": "2026-10-12T10:35:00-05:00"
                            }
                        ],
                        "sent": "2026-10-12T21:21:00-05:00",
                        "effective": "2026-10-12T21:21:00-05:00",
                        "onset": "2026-10-12T21:21:00-05:00",
                        "expires": "2026-10-13T09:30:00-05:00",
                        "ends": "2026-10-17T19:00:00-05:00",
                        "status": "Actual",
                        "messageType": "Update",
                        "category": "Met",
                        "severity": "Severe",
                        "certainty": "Observed",
                        "urgency": "Immediate",
                        "event": "Flood Warning",
                        "senderName": "NWS Lake Charles LA",
                        "headline": "Flood Warning issued October 12 at 9:21PM CDT until October 17 at 7:00PM CDT by NWS Lake Charles LA",
                        "description": "...The Flood Warning is extended for the following rivers in\nLouisiana...\n\nCalcasieu River Near Salt Water Barrier affecting Calcasieu Parish.",
                        "instruction": "Turn around, don't drown when encountering flooded roads.",
                        "response": "Avoid"
                    }
                }
            ],
            "title": "Current watches, warnings, and advisories for 30.25 N, 93.2 W",
            "updated": "2026-10-13T06:23:35+00:00"
        }
        """;

    // Three more from the NWS's whole list that morning, trimmed the same way: dense fog in Wisconsin, an air quality
    // alert in Michigan (whose severity is Unknown and which has no end), and a freeze watch in New Mexico for Thursday.
    private const string Elsewhere = """
        {
            "type": "FeatureCollection",
            "features": [
                {
                    "id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.05b4a7832f2bf04d3fd9b39766ca7454da713c7e.001.1",
                    "type": "Feature",
                    "geometry": null,
                    "properties": {
                        "id": "urn:oid:2.49.0.1.840.0.05b4a7832f2bf04d3fd9b39766ca7454da713c7e.001.1",
                        "areaDesc": "Sheboygan",
                        "references": [],
                        "sent": "2026-10-13T01:03:00-05:00",
                        "effective": "2026-10-13T01:03:00-05:00",
                        "onset": "2026-10-13T01:03:00-05:00",
                        "expires": "2026-10-13T06:00:00-05:00",
                        "ends": "2026-10-13T06:00:00-05:00",
                        "status": "Actual",
                        "messageType": "Alert",
                        "category": "Met",
                        "severity": "Moderate",
                        "certainty": "Likely",
                        "urgency": "Expected",
                        "event": "Dense Fog Advisory",
                        "senderName": "NWS Milwaukee/Sullivan WI",
                        "headline": "Dense Fog Advisory issued October 13 at 1:03AM CDT until October 13 at 6:00AM CDT by NWS Milwaukee/Sullivan WI",
                        "description": "* WHAT...Visibility as low as 1/4 mile in dense fog.",
                        "instruction": "If driving, slow down, use your headlights, and leave plenty of\ndistance ahead of you."
                    }
                },
                {
                    "id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.d87c3ff065f737ab723f2238c6fabc8aa26daee0.001.1",
                    "type": "Feature",
                    "geometry": null,
                    "properties": {
                        "id": "urn:oid:2.49.0.1.840.0.d87c3ff065f737ab723f2238c6fabc8aa26daee0.001.1",
                        "areaDesc": "Baraga; Marquette; Iron; Dickinson; Menominee",
                        "references": [],
                        "sent": "2026-10-12T10:39:00-04:00",
                        "effective": "2026-10-12T10:39:00-04:00",
                        "onset": "2026-10-12T10:39:00-04:00",
                        "expires": "2026-10-13T10:45:00-04:00",
                        "ends": null,
                        "status": "Actual",
                        "messageType": "Alert",
                        "category": "Met",
                        "severity": "Unknown",
                        "certainty": "Unknown",
                        "urgency": "Unknown",
                        "event": "Air Quality Alert",
                        "senderName": "NWS Marquette MI",
                        "headline": "Air Quality Alert issued October 12 at 10:39AM EDT by NWS Marquette MI",
                        "description": "The Michigan Department of Environmental Quality has issued an Air\nQuality Alert for Monday October 12.",
                        "instruction": null,
                        "note": "Non-Weather Emergency Message. Not sent to IPAWS."
                    }
                },
                {
                    "id": "https://api.weather.gov/alerts/urn:oid:2.49.0.1.840.0.e0df4adf2974a292beeb2dc95039314129353bb8.001.1",
                    "type": "Feature",
                    "geometry": null,
                    "properties": {
                        "id": "urn:oid:2.49.0.1.840.0.e0df4adf2974a292beeb2dc95039314129353bb8.001.1",
                        "areaDesc": "Upper Rio Grande Valley",
                        "references": [],
                        "sent": "2026-10-12T23:35:00-06:00",
                        "effective": "2026-10-12T23:35:00-06:00",
                        "onset": "2026-10-15T02:00:00-06:00",
                        "expires": "2026-10-13T13:00:00-06:00",
                        "ends": "2026-10-15T09:00:00-06:00",
                        "status": "Actual",
                        "messageType": "Alert",
                        "category": "Met",
                        "severity": "Severe",
                        "certainty": "Possible",
                        "urgency": "Future",
                        "event": "Freeze Watch",
                        "senderName": "NWS Albuquerque NM",
                        "headline": "Freeze Watch issued October 12 at 11:35PM MDT until October 15 at 9:00AM MDT by NWS Albuquerque NM",
                        "description": "* WHAT...Sub-freezing temperatures as low as 29 possible.",
                        "instruction": "Take steps now to protect tender plants from the cold."
                    }
                }
            ]
        }
        """;

    // A Met Office feed as it looks with warnings in it. Every feed was empty when these were written, so the items are
    // made the way the Met Office's are: the colour and type in the title, the times at the end of the description.
    private const string NorthWest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss xmlns:dc="http://purl.org/dc/elements/1.1/" version="2.0">
            <channel>
                <title>Met Office warnings for North West England</title>
                <link>https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings</link>
                <description>Weather warnings of severe and extreme weather from the Met Office</description>
                <language>en-gb</language>
                <copyright>(c) Crown copyright</copyright>
                <pubDate>Mon, 12 Oct 2026 10:30:00 GMT</pubDate>
                <item>
                    <title>Yellow warning of rain affecting North West England</title>
                    <link>https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?date=2026-10-13&amp;id=6a7c1e2b-41f0-4b4e-9d3c-0f5e2a8b7c11&amp;referrer=rss</link>
                    <description>Yellow warning of rain affecting North West England: Cumbria, Lancashire valid from 1500 Tue 13 Oct to 0600 Wed 14 Oct</description>
                    <pubDate>Mon, 12 Oct 2026 10:30:00 GMT</pubDate>
                    <guid isPermaLink="false">6a7c1e2b-41f0-4b4e-9d3c-0f5e2a8b7c11</guid>
                </item>
                <item>
                    <title>Amber warning of wind affecting North West England</title>
                    <link>https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?date=2026-10-14&amp;id=0b3d9f4a-2c5e-4f61-8a7b-9e1d2c3f4a5b&amp;referrer=rss</link>
                    <description>Amber warning of wind affecting North West England: Merseyside valid from 0300 Wed 14 Oct to 1200 Wed 14 Oct</description>
                    <pubDate>Mon, 12 Oct 2026 11:00:00 GMT</pubDate>
                    <guid isPermaLink="false">0b3d9f4a-2c5e-4f61-8a7b-9e1d2c3f4a5b</guid>
                </item>
            </channel>
        </rss>
        """;

    // The real London & South East feed with nothing in force, as every area's was on 13 October 2026.
    private const string Empty = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss xmlns:dc="http://purl.org/dc/elements/1.1/" version="2.0">
            <channel>
                <title>Met Office warnings for London &amp; South East England</title>
                <link>https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings</link>
                <description>Weather warnings of severe and extreme weather from the Met Office</description>
                <language>en-gb</language>
                <copyright>(c) Crown copyright</copyright>
                <pubDate>Sun, 27 Sep 2026 13:00:31 GMT</pubDate>
                <dc:creator>webteam@metoffice.gov.uk</dc:creator>
            </channel>
        </rss>
        """;

    private const string LakeCharlesPage = "https://forecast.weather.gov/MapClick.php?lat=30.25&lon=-93.2";
    private static readonly DateTimeOffset Morning = new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);
    private static readonly WarningsRequest Liverpool = new(WarningSource.MetOffice, 53.41, -2.98, "nw");
    private static readonly WarningsRequest LakeCharlesPlace = new(WarningSource.NationalWeatherService, 30.25, -93.2);

    [Fact]
    public void An_nws_answer_reads_each_alert()
    {
        var warnings = NationalWeatherService.Parse(LakeCharles, LakeCharlesPage);

        Assert.Equal(2, warnings.Count);
        var first = warnings[0];
        Assert.Equal("urn:oid:2.49.0.1.840.0.8c39b0a475d4541f891e248563cc28d2eb7d8526.002.1", first.Id);
        Assert.Equal("Flood Warning issued October 12 at 9:21PM CDT until October 15 at 1:00PM CDT by NWS Lake Charles LA", first.Headline);
        Assert.Equal(WarningLevel.Severe, first.Level);
        Assert.Equal("Severe", first.LevelText);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 21, 21, 0, TimeSpan.FromHours(-5)), first.Start);
        // When the flooding ends, not when this message runs out.
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.FromHours(-5)), first.End);
        Assert.Equal(LakeCharlesPage, first.Link);
        Assert.Equal(WarningSource.NationalWeatherService, first.Source);
        Assert.Equal(["urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.001.1"], first.Replaces);
        Assert.True(first.IsCritical);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 19, 0, 0, TimeSpan.FromHours(-5)), warnings[1].End);
    }

    [Fact]
    public void Nws_levels_and_times_are_taken_from_what_there_is()
    {
        var warnings = NationalWeatherService.Parse(Elsewhere, null);

        var fog = warnings[0];
        Assert.Equal(WarningLevel.Moderate, fog.Level);
        Assert.Equal("Moderate", fog.LevelText);
        Assert.False(fog.IsCritical);
        Assert.Empty(fog.Replaces);

        // No severity to give, and no end: then it lasts until the message runs out.
        var air = warnings[1];
        Assert.Equal(WarningLevel.Unknown, air.Level);
        Assert.Null(air.LevelText);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 10, 45, 0, TimeSpan.FromHours(-4)), air.End);

        // A watch for later in the week starts then, not when it was issued.
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 2, 0, 0, TimeSpan.FromHours(-6)), warnings[2].Start);
    }

    [Fact]
    public void Tests_exercises_and_cancellations_are_left_out()
    {
        var warnings = NationalWeatherService.Parse("""
            { "features": [
                { "properties": { "id": "a", "status": "Test", "severity": "Extreme", "headline": "Test message" } },
                { "properties": { "id": "b", "status": "Exercise", "severity": "Extreme", "headline": "Exercise" } },
                { "properties": { "id": "c", "status": "Actual", "messageType": "Cancel", "severity": "Severe", "headline": "Cancelled" } },
                { "properties": { "status": "Actual", "severity": "Severe", "headline": "No id" } },
                { "id": "https://api.weather.gov/alerts/d", "properties": { "status": "Actual", "severity": "Minor", "event": "Frost Advisory" } }
            ] }
            """, null);

        var only = Assert.Single(warnings);
        Assert.Equal("https://api.weather.gov/alerts/d", only.Id);
        Assert.Equal("Frost Advisory", only.Headline);
        Assert.Equal(WarningLevel.Minor, only.Level);
        Assert.Null(only.Start);
        Assert.Null(only.End);
        Assert.Empty(NationalWeatherService.Parse("""{ "type": "FeatureCollection", "features": [] }""", null));
    }

    [Fact]
    public void Something_that_isnt_an_nws_answer_is_rejected()
    {
        Assert.ThrowsAny<Exception>(() => NationalWeatherService.Parse("""{ "title": "Bad Request", "status": 400 }""", null));
        Assert.ThrowsAny<Exception>(() => NationalWeatherService.Parse("not json", null));
        Assert.ThrowsAny<Exception>(() => NationalWeatherService.Parse("[]", null));
    }

    [Fact]
    public void The_nws_is_asked_about_one_point_and_its_page_shows_the_alerts()
    {
        Assert.Equal("https://api.weather.gov/alerts/active?point=30.25,-93.2", NationalWeatherService.AlertsUrl(30.25, -93.2));
        // Four decimal places at most, or it answers with a redirect.
        Assert.Equal("https://api.weather.gov/alerts/active?point=40.7128,-74.006", NationalWeatherService.AlertsUrl(40.712776, -74.005974));
        Assert.Equal(LakeCharlesPage, NationalWeatherService.PageUrl(30.25, -93.2));
    }

    [Fact]
    public void A_met_office_feed_reads_each_warning()
    {
        var warnings = MetOfficeWarnings.Parse(NorthWest, Morning);

        Assert.Equal(2, warnings.Count);
        var rain = warnings[0];
        Assert.Equal("6a7c1e2b-41f0-4b4e-9d3c-0f5e2a8b7c11", rain.Id);
        Assert.Equal("Yellow warning of rain affecting North West England", rain.Headline);
        Assert.Equal(WarningLevel.Minor, rain.Level);
        Assert.Equal("Yellow", rain.LevelText);
        // UK time, which is BST in October.
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.FromHours(1)), rain.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 6, 0, 0, TimeSpan.FromHours(1)), rain.End);
        // The link as it is: the Met Office asks for its own page.
        Assert.Equal("https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?date=2026-10-13&id=6a7c1e2b-41f0-4b4e-9d3c-0f5e2a8b7c11&referrer=rss", rain.Link);
        Assert.Equal(WarningSource.MetOffice, rain.Source);
        Assert.False(rain.IsCritical);

        Assert.Equal(WarningLevel.Moderate, warnings[1].Level);
        Assert.Equal("Amber", warnings[1].LevelText);
        Assert.False(warnings[1].IsCritical);
    }

    [Fact]
    public void An_empty_met_office_feed_has_no_warnings() =>
        Assert.Empty(MetOfficeWarnings.Parse(Empty, Morning));

    [Fact]
    public void Met_office_items_are_read_tolerantly()
    {
        var warnings = MetOfficeWarnings.Parse("""
            <rss version="2.0"><channel>
                <item><title>Red warning of wind</title><link>javascript:alert(1)</link><description>Gusts of 90 mph</description></item>
                <item><title>Warning of fog affecting Wales</title><link>https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?id=fog</link></item>
                <item><title> </title><description>No title, so not a warning</description></item>
            </channel></rss>
            """, Morning);

        Assert.Equal(2, warnings.Count);
        var red = warnings[0];
        Assert.Equal(WarningLevel.Extreme, red.Level);
        Assert.Equal("Red", red.LevelText);
        Assert.True(red.IsCritical);
        // Only web pages: anything else goes to the Met Office's list of warnings instead.
        Assert.Equal(MetOfficeWarnings.WarningsPage, red.Link);
        Assert.Equal("Red warning of wind", red.Id);
        Assert.Null(red.Start);
        Assert.Null(red.End);

        var fog = warnings[1];
        Assert.Equal(WarningLevel.Unknown, fog.Level);
        Assert.Null(fog.LevelText);
        Assert.Equal("https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?id=fog", fog.Id);
    }

    [Fact]
    public void Times_without_a_year_take_the_one_nearest_the_feeds_date()
    {
        // Out on 30 December for New Year's Day: next year's, in GMT.
        var (start, end) = MetOfficeWarnings.Validity("Yellow warning of snow, ice valid from 1800 Fri 01 Jan to 1200 Sat 02 Jan", new DateTimeOffset(2026, 12, 30, 16, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 18, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero), end);

        // Over New Year: the end is in the year after the start.
        (start, end) = MetOfficeWarnings.Validity("valid from 1800 Thu 31 Dec to 0900 Fri 01 Jan", new DateTimeOffset(2026, 12, 30, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 9, 0, 0, TimeSpan.Zero), end);

        // Times that can't be read are left out rather than guessed.
        foreach (var unreadable in new[] { "valid from 2500 Tue 13 Oct to 0600 Wed 14 Oct", "valid from 1500 Tue 13 Foo to 0600 Wed 14 Oct", "Heavy rain this afternoon" })
        {
            (start, end) = MetOfficeWarnings.Validity(unreadable, Morning);
            Assert.Null(start);
            Assert.Null(end);
        }
    }

    [Fact]
    public void Something_that_isnt_a_feed_is_rejected()
    {
        Assert.Throws<FormatException>(() => MetOfficeWarnings.Parse("<html><body>Not found</body></html>", Morning));
        Assert.Throws<FormatException>(() => MetOfficeWarnings.Parse("not xml", Morning));
        // No entity tricks.
        Assert.Throws<FormatException>(() => MetOfficeWarnings.Parse(
            """<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY big "aaaaaaaaaa">]><rss><channel><item><title>&big;</title></item></channel></rss>""", Morning));
    }

    [Theory]
    [InlineData("Extreme", WarningLevel.Extreme)]
    [InlineData("Severe", WarningLevel.Severe)]
    [InlineData("Moderate", WarningLevel.Moderate)]
    [InlineData("Minor", WarningLevel.Minor)]
    [InlineData("Unknown", WarningLevel.Unknown)]
    [InlineData(null, WarningLevel.Unknown)]
    public void Nws_severities_are_the_levels(string? severity, WarningLevel level) =>
        Assert.Equal(level, NationalWeatherService.Level(severity));

    [Theory]
    [InlineData("Red", WarningLevel.Extreme, true)]
    [InlineData("amber", WarningLevel.Moderate, false)]
    [InlineData("YELLOW", WarningLevel.Minor, false)]
    [InlineData("green", WarningLevel.Unknown, false)]
    public void Met_office_colours_are_levels_with_red_alone_critical(string colour, WarningLevel level, bool critical)
    {
        Assert.Equal(level, MetOfficeWarnings.Level(colour));
        Assert.Equal(critical, Warning("x", level).IsCritical);
    }

    [Fact]
    public void Warnings_in_force_come_most_serious_first_and_ended_ones_go()
    {
        var soon = Warning("soon", WarningLevel.Moderate, start: Morning.AddHours(2), end: Morning.AddHours(8));
        var now = Warning("now", WarningLevel.Moderate, start: Morning.AddHours(-1), end: Morning.AddHours(5));
        var worst = Warning("worst", WarningLevel.Extreme, start: Morning.AddDays(1));
        var unknown = Warning("unknown", WarningLevel.Unknown);
        var minor = Warning("minor", WarningLevel.Minor, end: Morning.AddMinutes(1));
        var over = Warning("over", WarningLevel.Severe, end: Morning);

        var current = WeatherWarnings.Current([unknown, soon, over, minor, now, worst], Morning);

        Assert.Equal(["worst", "now", "soon", "minor", "unknown"], current.Select(w => w.Id));
        Assert.True(WarningLevel.Extreme > WarningLevel.Severe && WarningLevel.Severe > WarningLevel.Moderate
            && WarningLevel.Moderate > WarningLevel.Minor && WarningLevel.Minor > WarningLevel.Unknown);
    }

    [Theory]
    [InlineData("GB", WarningSource.MetOffice)]
    [InlineData("gb", WarningSource.MetOffice)]
    [InlineData("US", WarningSource.NationalWeatherService)]
    [InlineData("PR", WarningSource.NationalWeatherService)]
    [InlineData("GU", WarningSource.NationalWeatherService)]
    [InlineData("IE", WarningSource.None)]
    [InlineData("IM", WarningSource.None)]
    [InlineData("JE", WarningSource.None)]
    [InlineData("CA", WarningSource.None)]
    [InlineData("MX", WarningSource.None)]
    [InlineData("FR", WarningSource.None)]
    public void A_places_country_decides_whose_warnings_it_gets(string code, WarningSource source) =>
        // The country wins over the name and coordinates (these are London's).
        Assert.Equal(source, WeatherWarnings.SourceFor(code, "Somewhere, United States", 51.51, -0.13));

    [Theory]
    [InlineData("Leeds, England, United Kingdom", 53.8, -1.55, WarningSource.MetOffice)]
    [InlineData("Derry, Northern Ireland, United Kingdom", 55.0, -7.31, WarningSource.MetOffice)]
    [InlineData("Springfield, Missouri, United States", 37.22, -93.29, WarningSource.NationalWeatherService)]
    [InlineData("Paris, Île-de-France, France", 48.85, 2.35, WarningSource.None)]
    [InlineData("Dublin, Leinster, Ireland", 53.35, -6.26, WarningSource.None)]
    [InlineData("Toronto, Ontario, Canada", 43.65, -79.38, WarningSource.None)]
    public void Without_a_country_the_saved_name_says(string name, double latitude, double longitude, WarningSource source) =>
        Assert.Equal(source, WeatherWarnings.SourceFor(null, name, latitude, longitude));

    [Theory]
    [InlineData(51.51, -0.13, WarningSource.MetOffice)]   // London
    [InlineData(54.60, -5.93, WarningSource.MetOffice)]   // Belfast
    [InlineData(60.15, -1.15, WarningSource.MetOffice)]   // Lerwick
    [InlineData(58.21, -6.39, WarningSource.MetOffice)]   // Stornoway
    [InlineData(49.92, -6.30, WarningSource.MetOffice)]   // The Isles of Scilly
    [InlineData(55.43, -5.61, WarningSource.MetOffice)]   // Campbeltown
    [InlineData(52.48, 1.75, WarningSource.MetOffice)]    // Lowestoft
    [InlineData(51.13, 1.31, WarningSource.MetOffice)]    // Dover
    [InlineData(53.35, -6.26, WarningSource.None)]        // Dublin
    [InlineData(51.90, -8.47, WarningSource.None)]        // Cork
    [InlineData(54.95, -7.73, WarningSource.None)]        // Letterkenny
    [InlineData(55.23, -6.93, WarningSource.None)]        // Inishowen Head, Donegal
    [InlineData(54.15, -4.48, WarningSource.None)]        // Douglas, Isle of Man
    [InlineData(49.19, -2.11, WarningSource.None)]        // St Helier, Jersey
    [InlineData(50.95, 1.85, WarningSource.None)]         // Calais
    [InlineData(50.73, 1.61, WarningSource.None)]         // Boulogne
    [InlineData(40.71, -74.01, WarningSource.None)]       // New York: coordinates alone never go to the NWS
    [InlineData(43.65, -79.38, WarningSource.None)]       // Toronto
    public void Coordinates_alone_find_great_britain_and_never_ireland(double latitude, double longitude, WarningSource source) =>
        Assert.Equal(source, WeatherWarnings.SourceFor(null, "51.5, -0.12", latitude, longitude));

    [Theory]
    [InlineData(53.41, -2.98, "nw")]   // Liverpool
    [InlineData(51.48, -3.18, "wl")]   // Cardiff
    [InlineData(54.60, -5.93, "ni")]   // Belfast
    [InlineData(55.86, -4.25, "st")]   // Glasgow
    [InlineData(51.51, -0.13, "se")]   // London
    [InlineData(53.80, -1.55, "yh")]   // Leeds
    [InlineData(55.95, -3.19, "dg")]   // Edinburgh
    [InlineData(57.15, -2.09, "gr")]   // Aberdeen
    [InlineData(57.48, -4.22, "he")]   // Inverness
    [InlineData(58.44, -3.09, "he")]   // Wick, nearer Orkney than Inverness
    [InlineData(58.98, -2.96, "os")]   // Kirkwall
    [InlineData(56.46, -2.97, "ta")]   // Dundee
    [InlineData(54.98, -1.62, "ne")]   // Newcastle
    [InlineData(52.48, -1.90, "wm")]   // Birmingham
    [InlineData(52.95, -1.15, "em")]   // Nottingham
    [InlineData(52.63, 1.30, "ee")]    // Norwich
    [InlineData(51.45, -2.59, "sw")]   // Bristol
    [InlineData(50.38, -4.14, "sw")]   // Plymouth
    [InlineData(53.19, -2.89, "nw")]   // Chester, by the Welsh border
    [InlineData(53.40, -2.16, "nw")]   // Stockport
    [InlineData(53.44, -1.95, "em")]   // Glossop, over the hill in Derbyshire
    public void A_uk_place_gets_the_met_office_area_it_is_in(double latitude, double longitude, string area) =>
        Assert.Equal(area, MetOfficeWarnings.AreaFor(latitude, longitude).Code);

    [Fact]
    public void There_are_sixteen_areas_found_by_code()
    {
        Assert.Equal(16, MetOfficeWarnings.Areas.Count);
        Assert.Equal(16, MetOfficeWarnings.Areas.Select(a => a.Code).Distinct().Count());
        Assert.Equal("North West England", MetOfficeWarnings.Find("NW")?.Name);
        Assert.Null(MetOfficeWarnings.Find("auto"));
        Assert.Null(MetOfficeWarnings.Find(null));
        Assert.Equal("https://www.metoffice.gov.uk/public/data/PWSCache/WarningsRSS/Region/nw", MetOfficeWarnings.FeedUrl("nw"));
    }

    [Fact]
    public async Task Only_a_us_place_is_ever_sent_to_the_nws()
    {
        var nws = new List<string>();
        var metOffice = new List<string>();
        Task<string> Nws(string url, CancellationToken _)
        {
            nws.Add(url);
            return Task.FromResult(LakeCharles);
        }
        Task<string> MetOffice(string url, CancellationToken _)
        {
            metOffice.Add(url);
            return Task.FromResult(NorthWest);
        }

        // A UK place: only its area's feed, with nothing about where it is but the area.
        var uk = await WeatherWarnings.FetchAsync(Liverpool, Nws, MetOffice, Morning, CancellationToken.None);
        Assert.Equal(2, uk.Count);
        Assert.Empty(nws);
        Assert.Equal(["https://www.metoffice.gov.uk/public/data/PWSCache/WarningsRSS/Region/nw"], metOffice);

        // Anywhere else: nothing at all.
        Assert.Empty(await WeatherWarnings.FetchAsync(new WarningsRequest(WarningSource.None, 48.85, 2.35), Nws, MetOffice, Morning, CancellationToken.None));
        Assert.Empty(await WeatherWarnings.FetchAsync(new WarningsRequest(WarningSource.MetOffice, 51.5, -0.12, "xx"), Nws, MetOffice, Morning, CancellationToken.None));
        Assert.Empty(nws);
        Assert.Single(metOffice);

        // A US place: its coordinates to the NWS, and each alert links to its forecast page.
        var us = await WeatherWarnings.FetchAsync(LakeCharlesPlace, Nws, MetOffice, Morning, CancellationToken.None);
        Assert.Equal(["https://api.weather.gov/alerts/active?point=30.25,-93.2"], nws);
        Assert.All(us, w => Assert.Equal(LakeCharlesPage, w.Link));
    }

    [Fact]
    public async Task Nothing_is_fetched_or_shown_while_it_is_off()
    {
        var fetches = 0;
        WarningsRequest? wanted = null;
        var time = new FakeTime(Morning);
        var provider = new WeatherWarningsProvider(() => wanted, (_, _) =>
        {
            fetches++;
            return Task.FromResult(MetOfficeWarnings.Parse(NorthWest, Morning));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        for (var i = 0; i < 5; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromHours(1));
        }
        Assert.Equal(0, fetches);
        Assert.All(store.Definitions.Where(d => d.Category == "Weather warnings"), d => Assert.False(store.TryGet(d.Id, out var sample) && sample.HasValue, d.Id));
        Assert.Null(store.GetAttachment(WeatherWarningsProvider.Warnings));

        // Turned on: straight away.
        time.Advance(Morning - time.GetUtcNow());
        wanted = Liverpool;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal("2", Format(store, WeatherWarningsProvider.Count));

        // Turned off again: the warnings go and nothing more is fetched.
        wanted = null;
        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromHours(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherWarningsProvider.Count));
        Assert.Null(store.GetAttachment(WeatherWarningsProvider.Warnings));
    }

    [Fact]
    public async Task The_most_serious_warning_is_published_with_them_all()
    {
        var provider = new WeatherWarningsProvider(() => Liverpool, (_, _) => Task.FromResult(MetOfficeWarnings.Parse(NorthWest, Morning)), new FakeTime(Morning));
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal("2", Format(store, WeatherWarningsProvider.Count));
        // Amber before yellow, though it's later.
        Assert.Equal("Amber warning of wind affecting North West England", Format(store, WeatherWarningsProvider.Headline));
        Assert.Equal("Amber", Format(store, WeatherWarningsProvider.Level));
        var warnings = Assert.IsAssignableFrom<IReadOnlyList<WeatherWarning>>(store.GetAttachment(WeatherWarningsProvider.Warnings));
        Assert.Equal(["Amber", "Yellow"], warnings.Select(w => w.LevelText));
    }

    [Fact]
    public async Task None_in_force_is_a_count_of_nothing_and_no_list()
    {
        var provider = new WeatherWarningsProvider(() => Liverpool, (_, _) => Task.FromResult(MetOfficeWarnings.Parse(Empty, Morning)), new FakeTime(Morning));
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal("0", Format(store, WeatherWarningsProvider.Count));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherWarningsProvider.Headline));
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherWarningsProvider.Level));
        Assert.Null(store.GetAttachment(WeatherWarningsProvider.Warnings));
    }

    [Fact]
    public async Task Warnings_refresh_every_ten_minutes_retry_failures_and_are_kept_a_while()
    {
        var time = new FakeTime(Morning);
        var requests = 0;
        var fail = false;
        var provider = new WeatherWarningsProvider(() => Liverpool, (_, _) =>
        {
            requests++;
            return fail ? Task.FromException<IReadOnlyList<WeatherWarning>>(new HttpRequestException("offline")) : Task.FromResult(MetOfficeWarnings.Parse(NorthWest, Morning));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(9.5));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, requests);
        time.Advance(TimeSpan.FromMinutes(0.5));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, requests);
        var lastFetched = time.GetUtcNow();

        // Due again, but offline: the warnings stay, and it tries again a few minutes later.
        fail = true;
        time.Advance(WeatherWarningsProvider.RefreshEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests);
        Assert.Equal("2", Format(store, WeatherWarningsProvider.Count));
        time.Advance(WeatherWarningsProvider.RetryAfter - TimeSpan.FromSeconds(30));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(3, requests);
        time.Advance(TimeSpan.FromSeconds(30));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(4, requests);

        // An hour after the last fetch that worked, they're too old to go on.
        time.Advance(lastFetched + WeatherWarningsProvider.KeepFor - time.GetUtcNow());
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("2", Format(store, WeatherWarningsProvider.Count));
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherWarningsProvider.Count));
        Assert.Null(store.GetAttachment(WeatherWarningsProvider.Warnings));
    }

    [Fact]
    public async Task A_warning_goes_when_it_ends_without_waiting_for_the_next_fetch()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 14, 4, 55, 0, TimeSpan.Zero));
        var provider = new WeatherWarningsProvider(() => Liverpool, (_, _) => Task.FromResult(MetOfficeWarnings.Parse(NorthWest, Morning)), time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("2", Format(store, WeatherWarningsProvider.Count));

        // The rain warning ends at 06:00 BST (05:00 UTC).
        time.Advance(TimeSpan.FromMinutes(5));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("1", Format(store, WeatherWarningsProvider.Count));
        Assert.Equal("Amber", Format(store, WeatherWarningsProvider.Level));
    }

    [Fact]
    public async Task Another_place_or_area_fetches_straight_away_and_never_shows_the_last_ones()
    {
        var time = new FakeTime(Morning);
        var requests = new List<WarningsRequest>();
        var fail = false;
        var wanted = Liverpool;
        var provider = new WeatherWarningsProvider(() => wanted, (request, _) =>
        {
            requests.Add(request);
            return fail ? Task.FromException<IReadOnlyList<WeatherWarning>>(new HttpRequestException("offline")) : Task.FromResult(MetOfficeWarnings.Parse(NorthWest, Morning));
        }, time);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        // Elsewhere in the same area: no need to ask again.
        wanted = Liverpool with { Latitude = 53.48, Longitude = -2.24 };
        time.Advance(TimeSpan.FromMinutes(1));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single(requests);

        // Another area, while offline: North West England's warnings would be wrong there.
        fail = true;
        wanted = Liverpool with { Area = "wl" };
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, requests.Count);
        Assert.Equal("wl", requests[^1].Area);
        Assert.Equal(MetricFormatter.Missing, Format(store, WeatherWarningsProvider.Count));
        Assert.Null(store.GetAttachment(WeatherWarningsProvider.Warnings));
    }

    [Fact]
    public async Task It_runs_on_the_weathers_loop_rather_than_one_of_its_own()
    {
        var fetches = 0;
        WarningsRequest? wanted = null;
        var warnings = new WeatherWarningsProvider(() => wanted, (_, _) =>
        {
            fetches++;
            return Task.FromResult(NationalWeatherService.Parse(LakeCharles, LakeCharlesPage));
        }, new FakeTime(Morning));
        var weather = new WeatherProvider(() => null, warnings: warnings);
        var store = new MetricStore();
        weather.Initialize(store);

        // Its readings are there to pick in the editor, but nothing is fetched while it's off.
        Assert.Contains(store.Definitions, d => d.Id == WeatherWarningsProvider.Headline);
        await weather.SampleAsync(store, CancellationToken.None);
        Assert.Equal(0, fetches);

        wanted = LakeCharlesPlace;
        await weather.SampleAsync(store, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.Equal("Severe", Format(store, WeatherWarningsProvider.Level));
    }

    [Fact]
    public void Each_warning_is_notified_once()
    {
        var notifications = new WarningNotifications();
        var rain = Warning("rain", WarningLevel.Minor, "Yellow warning of rain affecting Wales");
        var wind = Warning("wind", WarningLevel.Moderate, "Amber warning of wind affecting Wales");

        var first = notifications.Next([rain]);
        Assert.Equal(new WarningNotice("Weather warning", "Yellow warning of rain affecting Wales", "https://example.com/rain"), first);
        Assert.Null(notifications.Next([rain]));

        // Only what's new: the wind, not the rain again.
        Assert.Equal("Amber warning of wind affecting Wales", notifications.Next([wind, rain])?.Message);
        Assert.Null(notifications.Next([wind, rain]));
        // Gone and back again is the same warning.
        Assert.Null(notifications.Next([]));
        Assert.Null(notifications.Next(null));
        Assert.Null(notifications.Next([rain]));
    }

    [Fact]
    public void A_warning_that_gets_more_serious_is_notified_again()
    {
        var notifications = new WarningNotifications();
        notifications.Next([Warning("wind", WarningLevel.Minor, "Yellow warning of wind")]);

        Assert.Equal("Amber warning of wind", notifications.Next([Warning("wind", WarningLevel.Moderate, "Amber warning of wind")])?.Message);
        // Less serious again isn't news, and nor is going back up to where it was.
        Assert.Null(notifications.Next([Warning("wind", WarningLevel.Minor, "Yellow warning of wind")]));
        Assert.Null(notifications.Next([Warning("wind", WarningLevel.Moderate, "Amber warning of wind")]));
    }

    [Fact]
    public void An_nws_update_with_an_id_of_its_own_is_the_same_warning()
    {
        var notifications = new WarningNotifications();
        notifications.Next([Warning("first", WarningLevel.Severe)]);

        Assert.Null(notifications.Next([Warning("second", WarningLevel.Severe) with { Replaces = ["first"] }]));
        // An update of the update, too.
        Assert.Null(notifications.Next([Warning("third", WarningLevel.Severe) with { Replaces = ["second"] }]));
        // Unless it's more serious.
        Assert.NotNull(notifications.Next([Warning("fourth", WarningLevel.Extreme) with { Replaces = ["third"] }]));

        // The real thing: two updates of flood warnings told about before.
        notifications.Next([Warning("urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.001.1", WarningLevel.Severe),
            Warning("urn:oid:2.49.0.1.840.0.f1538ea86fecb1810e6746fdff8521e0ac6e09e1.002.1", WarningLevel.Severe)]);
        Assert.Null(notifications.Next(NationalWeatherService.Parse(LakeCharles, LakeCharlesPage)));
    }

    [Fact]
    public void Several_at_once_make_one_notification_listing_the_worst_few()
    {
        var notifications = new WarningNotifications();
        var warnings = Enumerable.Range(1, 5).Select(i => Warning($"w{i}", WarningLevel.Moderate, $"Warning {i}")).ToList();

        var notice = notifications.Next(warnings)!;
        Assert.Equal("5 weather warnings", notice.Title);
        Assert.Equal("Warning 1\nWarning 2\nWarning 3\nand 2 more", notice.Message);
        Assert.Equal("https://example.com/w1", notice.Link);
        // The two counted aren't news later either.
        Assert.Null(notifications.Next(warnings.Skip(3).ToList()));

        // Windows cuts off text past 255 characters, so long headlines are shortened to fit.
        var nws = NationalWeatherService.Parse(LakeCharles, LakeCharlesPage).Concat(NationalWeatherService.Parse(Elsewhere, null)).ToList();
        var long1 = new WarningNotifications().Next(nws)!;
        Assert.True(long1.Message.Length <= 255, long1.Message);
        Assert.EndsWith("\nand 2 more", long1.Message);
        Assert.Equal(4, long1.Message.Split('\n').Length);
        var one = new WarningNotifications().Next([Warning("long", WarningLevel.Severe, new string('x', 400))])!;
        Assert.Equal(255, one.Message.Length);
        Assert.EndsWith("…", one.Message);
    }

    private static WeatherWarning Warning(string id, WarningLevel level, string? headline = null, DateTimeOffset? start = null, DateTimeOffset? end = null) =>
        new(id, headline ?? id, level, level.ToString(), start, end, $"https://example.com/{id}", WarningSource.MetOffice);

    private static string Format(MetricStore store, string id) =>
        store.TryGet(id, out var sample) ? MetricFormatter.Format(sample) : "(missing)";

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
