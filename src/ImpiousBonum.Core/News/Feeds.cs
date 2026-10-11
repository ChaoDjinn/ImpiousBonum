using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ImpiousBonum.Core.News;

/// <summary>One headline from a feed.</summary>
/// <param name="Link">The article's web address, or null if the feed didn't give one.</param>
/// <param name="Published">When it was published, if the feed says.</param>
/// <param name="Source">The name of the feed it came from.</param>
public sealed record Headline(string Title, string? Link, DateTimeOffset? Published, string Source);

/// <summary>
/// Reads news feeds: RSS 2.0, RSS 1.0 (RDF) and Atom, which between them cover nearly every site, blog, YouTube channel
/// and subreddit. Feeds come from anywhere, so DTDs are refused (no entity tricks) and titles lose any HTML.
/// </summary>
public static partial class Feeds
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Rss1 = "http://purl.org/rss/1.0/";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>The headlines in <paramref name="xml"/>, in the feed's order. Throws <see cref="FormatException"/> if it isn't a feed.</summary>
    public static IReadOnlyList<Headline> Parse(string xml, string source)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 20_000_000,
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"it isn't a news feed ({ex.Message})", ex);
        }

        var root = document.Root ?? throw new FormatException("it isn't a news feed");
        IEnumerable<Headline> items = root.Name.LocalName switch
        {
            "rss" => root.Element("channel")?.Elements("item").Select(i => Item(i, source, i.Element("title"), i.Element("link")?.Value, i.Element("pubDate")?.Value ?? i.Element(DublinCore + "date")?.Value)) ?? [],
            "feed" when root.Name.Namespace == Atom => root.Elements(Atom + "entry").Select(e => Item(e, source, e.Element(Atom + "title"), AtomLink(e), e.Element(Atom + "published")?.Value ?? e.Element(Atom + "updated")?.Value)),
            "RDF" => root.Elements(Rss1 + "item").Select(i => Item(i, source, i.Element(Rss1 + "title"), i.Element(Rss1 + "link")?.Value, i.Element(DublinCore + "date")?.Value)),
            _ => throw new FormatException("it isn't a news feed (RSS or Atom)"),
        };
        return items.Where(h => h.Title.Length > 0).ToList();
    }

    private static Headline Item(XElement item, string source, XElement? title, string? link, string? date) =>
        new(Clean(title?.Value ?? string.Empty), WebLink(link), ParseDate(date), source);

    /// <summary>An Atom entry's page: the <c>alternate</c> link, or the first one without a rel.</summary>
    private static string? AtomLink(XElement entry)
    {
        var links = entry.Elements(Atom + "link").ToList();
        var link = links.FirstOrDefault(l => (string?)l.Attribute("rel") == "alternate") ?? links.FirstOrDefault(l => l.Attribute("rel") is null);
        return (string?)link?.Attribute("href");
    }

    /// <summary>Only http and https: a feed shouldn't be able to make a tap run something else.</summary>
    internal static string? WebLink(string? link) =>
        Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;

    /// <summary>Plain text: tags gone, entities decoded, whitespace collapsed.</summary>
    internal static string Clean(string text)
    {
        var plain = WebUtility.HtmlDecode(Tags().Replace(text, " "));
        return Spaces().Replace(plain, " ").Trim();
    }

    /// <summary>RFC 822 dates (RSS: "Sun, 11 Oct 2026 09:30:00 GMT" or "+0100") and ISO 8601 (Atom), else null.</summary>
    internal static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        // RFC 822 time zone names .NET doesn't know: swap them for offsets.
        var named = ZoneName().Match(text);
        if (named.Success && ZoneOffsets.TryGetValue(named.Groups[1].Value.ToUpperInvariant(), out var offset))
        {
            var replaced = text[..named.Groups[1].Index] + offset;
            if (DateTimeOffset.TryParseExact(replaced, ["ddd, d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm zzz"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed;
        }
        return null;
    }

    private static readonly Dictionary<string, string> ZoneOffsets = new()
    {
        ["UT"] = "+00:00", ["GMT"] = "+00:00", ["Z"] = "+00:00",
        ["EST"] = "-05:00", ["EDT"] = "-04:00", ["CST"] = "-06:00", ["CDT"] = "-05:00",
        ["MST"] = "-07:00", ["MDT"] = "-06:00", ["PST"] = "-08:00", ["PDT"] = "-07:00",
        ["BST"] = "+01:00", ["CET"] = "+01:00", ["CEST"] = "+02:00",
    };

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s([A-Za-z]{1,4})$")]
    private static partial Regex ZoneName();
}
