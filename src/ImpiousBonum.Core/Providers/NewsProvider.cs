using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.News;

namespace ImpiousBonum.Core.Providers;

/// <summary>A news feed's address (RSS or Atom) and the name to show with its headlines.</summary>
public sealed record NewsFeed(string Name, string Url);

/// <summary>One feed's own headlines, in the feed's order (newest first), by the feed's name.</summary>
public sealed record FeedHeadlines(string Feed, IReadOnlyList<Headline> Headlines);

/// <summary>
/// Headlines from your news feeds: each fetched every 15 minutes, the newest as metrics, and all of them (an
/// <c>IReadOnlyList&lt;Headline&gt;</c>, newest first) as an attachment for the news widget, with each feed's own as
/// another. Nothing is fetched until a feed is added.
/// </summary>
/// <param name="feeds">The feeds; read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets a feed's XML; HTTP unless a test swaps it.</param>
public sealed class NewsProvider(
    Func<IReadOnlyList<NewsFeed>> feeds,
    Func<string, CancellationToken, Task<string>>? fetch = null,
    TimeProvider? time = null) : IMetricProvider
{
    public const string Title = "news.title";
    public const string Source = "news.source";
    public const string Age = "news.age";
    public const string Status = "news.status";

    /// <summary>The headlines, newest first, as a <see cref="MetricStore"/> attachment.</summary>
    public const string Headlines = "news.headlines";

    /// <summary>
    /// Each feed's own headlines (an <c>IReadOnlyList&lt;FeedHeadlines&gt;</c>, in the News feeds page's order) as an
    /// attachment, for a ticker per feed. A story two feeds share is in both.
    /// </summary>
    public const string ByFeed = "news.feeds";

    /// <summary>Most headlines kept from each feed.</summary>
    public const int PerFeed = 30;

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private static readonly HttpClient Http = CreateClient();

    private readonly Func<string, CancellationToken, Task<string>> _fetch = fetch ?? GetAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, Fetched> _feeds = new();

    private sealed class Fetched
    {
        public IReadOnlyList<Headline> Items = [];
        public DateTimeOffset NextTry;
        public string? Problem;
        public bool Got;
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Title, "News: newest headline", "News", MetricUnit.Text));
        store.Register(new MetricDefinition(Source, "News: where the newest headline is from", "News", MetricUnit.Text));
        store.Register(new MetricDefinition(Age, "News: how long ago the newest headline came out, e.g. 12 min ago", "News", MetricUnit.Text));
        store.Register(new MetricDefinition(Status, "News: status", "News", MetricUnit.Text));
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var wanted = feeds().Where(f => !string.IsNullOrWhiteSpace(f.Url)).ToList();
        foreach (var gone in _feeds.Keys.Where(k => wanted.All(f => Key(f) != k)).ToList())
            _feeds.Remove(gone);

        foreach (var feed in wanted)
        {
            if (!_feeds.TryGetValue(Key(feed), out var fetched))
                _feeds[Key(feed)] = fetched = new Fetched { NextTry = now };
            if (now < fetched.NextTry)
                continue;
            try
            {
                var xml = await _fetch(feed.Url.Trim(), cancellationToken).ConfigureAwait(false);
                fetched.Items = Feeds.Parse(xml, feed.Name).Take(PerFeed).ToList();
                fetched.Problem = null;
                fetched.Got = true;
                fetched.NextTry = now + RefreshEvery;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or UriFormatException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                fetched.Problem = ex is TaskCanceledException ? "it took too long to answer" : ex.Message;
                fetched.NextTry = now + RetryAfter;
            }
        }

        var byFeed = wanted.Select(f => new FeedHeadlines(f.Name, _feeds.TryGetValue(Key(f), out var fetched) ? fetched.Items : [])).ToList();
        var headlines = Merge(byFeed.Select(f => f.Headlines).ToList());
        var newest = headlines.FirstOrDefault();
        store.SetText(Title, newest?.Title);
        store.SetText(Source, newest?.Source);
        store.SetText(Age, newest?.Published is { } published ? Ago(now - published) : null);
        store.SetText(Status, Describe(wanted, headlines.Count));
        store.SetAttachment(Headlines, headlines.Count > 0 ? headlines : null);
        store.SetAttachment(ByFeed, headlines.Count > 0 ? byFeed : null);
    }

    /// <summary>
    /// Every feed's headlines, newest first. A headline with no date keeps its place among its own feed's (feeds list
    /// newest first), taking the date of the dated one before it.
    /// </summary>
    internal static IReadOnlyList<Headline> Merge(IReadOnlyList<IReadOnlyList<Headline>> perFeed)
    {
        var all = new List<(Headline Headline, DateTimeOffset SortBy, int Feed, int Index)>();
        for (var f = 0; f < perFeed.Count; f++)
        {
            var carried = DateTimeOffset.MaxValue;
            for (var i = 0; i < perFeed[f].Count; i++)
            {
                var headline = perFeed[f][i];
                carried = headline.Published ?? carried;
                all.Add((headline, carried, f, i));
            }
        }
        return all
            .OrderByDescending(h => h.SortBy)
            .ThenBy(h => h.Index)
            .ThenBy(h => h.Feed)
            // The same story in two feeds shows once.
            .DistinctBy(h => h.Headline.Link ?? h.Headline.Title, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.Headline)
            .ToList();
    }

    private string? Describe(IReadOnlyList<NewsFeed> wanted, int count)
    {
        if (wanted.Count == 0)
            return null;
        var problems = wanted
            .Where(f => _feeds.TryGetValue(Key(f), out var fetched) && fetched.Problem is not null)
            .Select(f => $"couldn't read {f.Name}: {_feeds[Key(f)].Problem}")
            .ToList();
        if (problems.Count > 0)
        {
            var text = string.Join("; ", problems);
            return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
        }
        if (!_feeds.Values.Any(f => f.Got))
            return "Fetching…";
        return count == 1 ? "1 headline" : $"{count} headlines";
    }

    /// <summary>"just now", "12 min ago", "3 h ago", "2 days ago".</summary>
    public static string Ago(TimeSpan age) =>
        age.TotalMinutes < 1 ? "just now"
        : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
        : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
        : (int)age.TotalDays == 1 ? "1 day ago"
        : $"{(int)age.TotalDays} days ago";

    private static string Key(NewsFeed feed) => feed.Url.Trim();

    private static Task<string> GetAsync(string url, CancellationToken cancellationToken) => Http.GetStringAsync(url, cancellationToken);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebIdentity.UserAgent);
        return client;
    }

    public void Dispose()
    {
    }
}
