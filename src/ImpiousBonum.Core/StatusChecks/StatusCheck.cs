using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.StatusChecks;

/// <summary>
/// One of your own servers or services to keep an eye on: a host name or IP address, which is pinged, or a web address,
/// whose page is asked for. Each check has the readings <c>status.&lt;name&gt;.up</c>, <c>.ms</c> and <c>.since</c>
/// (see <see cref="IdFor"/>). Checks are per PC (settings.json), never part of a layout, so a layout from someone else
/// can't make the dashboard contact anything.
/// </summary>
/// <param name="Name">What the dashboard calls it, e.g. NAS. In lower case, with a hyphen for spaces, it's the middle of the readings' ids.</param>
/// <param name="Address">A host name or IP address to ping (<c>nas.local</c>, <c>192.168.1.10</c>), or an http:// or https:// address.</param>
/// <param name="Expect">For a web address: a word the page must contain to count as up. Empty for any page that loads.</param>
/// <param name="Every">Seconds between checks (at least <see cref="MinimumEvery"/>).</param>
/// <param name="AnyCertificate">For an https address: accept a certificate Windows doesn't trust, as home servers often have their own.</param>
public sealed record StatusCheck(string Name, string Address, string? Expect = null, int Every = StatusCheck.DefaultEvery, bool AnyCertificate = false)
{
    public const int DefaultEvery = 60;

    public const int MinimumEvery = 10;

    public const string Prefix = "status.";

    /// <summary>The last part of each check's reading ids: up or down (1 or 0), the response time, and how long it's been either.</summary>
    public const string Up = "up", Ms = "ms", Since = "since";

    /// <summary>An http:// or https:// address, asked for its page; anything else is pinged.</summary>
    [JsonIgnore]
    public bool IsWeb => IsWebAddress(Address);

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(MinimumEvery, Every));

    /// <summary>The middle of the reading ids: "Pi-hole" is <c>pi-hole</c>, "Home Assistant" <c>home-assistant</c>.</summary>
    [JsonIgnore]
    public string Slug => MetricIdPart.From(Name ?? string.Empty);

    /// <summary>One of the check's readings: <c>status.nas.up</c> for <see cref="Up"/> of a check called NAS.</summary>
    public string IdFor(string reading) => IdFor(Name ?? string.Empty, reading)!;

    /// <summary>One of a check's readings by its name, or null for a name with no letters or digits.</summary>
    public static string? IdFor(string name, string reading) =>
        MetricIdPart.From(name) is { Length: > 0 } part ? $"{Prefix}{part}.{reading}" : null;

    /// <summary>All the settings as one string, to tell when a check was edited.</summary>
    [JsonIgnore]
    public string Key => JsonSerializer.Serialize(this);

    public static bool IsWebAddress(string? address) =>
        address?.Trim() is { } trimmed
        && (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why this check can't be used, or null if it can.</summary>
    public string? Problem()
    {
        var name = Name?.Trim() ?? string.Empty;
        if (Slug.Length == 0)
            return "Give it a name with a letter or digit in it, such as NAS.";
        // A status widget lists the ones it shows by name, separated by commas, or says all for every one.
        if (name.Contains(','))
            return "A check's name can't have a comma in it.";
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            return "A check can't be called all.";
        var address = Address?.Trim() ?? string.Empty;
        if (address.Length == 0)
            return "Give it an address: a host name or IP address, or a web address.";
        if (IsWeb)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
                return "That web address isn't complete: it should look like http://nas.local:5000.";
        }
        else if (address.Contains("://", StringComparison.Ordinal))
        {
            return "Only web addresses starting http:// or https:// can be asked for a page. For anything else, give its host name or IP address.";
        }
        else if (Uri.CheckHostName(address) is UriHostNameType.Unknown or UriHostNameType.Basic)
        {
            return address.Contains(':') || address.Contains('/')
                ? $"To check a port or a page, give its web address, such as http://{address}."
                : "Give a host name or IP address, such as nas.local or 192.168.1.10, or a web address starting http:// or https://.";
        }
        if (Every < MinimumEvery)
            return $"Check it every {MinimumEvery} seconds or more.";
        return null;
    }
}

/// <summary>What one check found.</summary>
/// <param name="Up">Whether it answered: a ping's reply, or a page that loaded (with the word, if one is expected).</param>
/// <param name="Milliseconds">How long the answer took, while it's up.</param>
/// <param name="Detail">Why it's down, in a few words, e.g. "No answer" or "HTTP 503 Service Unavailable"; null while it's up.</param>
public sealed record CheckResult(bool Up, double? Milliseconds, string? Detail)
{
    public static CheckResult Answered(double milliseconds) => new(true, milliseconds, null);

    public static CheckResult Down(string detail) => new(false, null, detail);
}
